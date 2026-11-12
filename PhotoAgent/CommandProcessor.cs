using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace PhotoAgent
{
    public static class CommandProcessor
    {
        private const string ResponsesFolder = @"C:\psa_photos\responses";
        private static readonly Dictionary<string, string> CompletedResponses =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static bool Process(string commandFile)
        {
            return Process(commandFile, ResponsesFolder);
        }

        internal static bool Process(string commandFile, string responsesFolder)
        {
            CaptureCommand command;
            if (!TryReadCommand(commandFile, out command))
                return false;

            string responseFile = Path.Combine(responsesFolder, command.request_id + ".done");
            try
            {
                string completedResponse;
                lock (CompletedResponses)
                {
                    CompletedResponses.TryGetValue(responseFile, out completedResponse);
                }

                if (completedResponse != null)
                {
                    PublishResponse(commandFile, responseFile, completedResponse);
                    return true;
                }

                if (File.Exists(responseFile))
                {
                    File.Delete(commandFile);
                    return true;
                }

                if (!CaptureState.TryBegin(command.request_id))
                {
                    // A second file for the active request must wait for its real result.
                    if (string.Equals(CaptureState.RequestId, command.request_id,
                        StringComparison.OrdinalIgnoreCase))
                        return false;

                    string busyJson = new JavaScriptSerializer().Serialize(new
                    {
                        status = "busy",
                        request_id = command.request_id,
                        document_number = command.document_number,
                        message = "PhotoAgent is busy"
                    });
                    PublishResponse(commandFile, responseFile, busyJson);
                    return true;
                }

                try
                {
                    LogManager.Write("Request received: " + command.request_id
                        + "; command: " + command.command);

                    CaptureResult result;
                    if (command.command == "capture_document")
                        result = CaptureWorkflow.CaptureDocument(command);
                    else if (command.command == "capture_extra")
                        result = CaptureWorkflow.CaptureExtra(command);
                    else
                        throw new InvalidOperationException("Unknown command: " + command.command);

                    if (CaptureState.TimedOut)
                        throw new TimeoutException("Capture timeout");
                    if (!result.Success)
                        throw new InvalidOperationException("Capture cancelled or failed");

                    Complete(commandFile, responseFile, new
                    {
                        status = "ok",
                        request_id = command.request_id,
                        document_number = command.document_number,
                        files = result.Files
                    });
                }
                catch (Exception ex)
                {
                    // A publishing failure is retried using the cached result, without recapture.
                    lock (CompletedResponses)
                    {
                        if (CompletedResponses.ContainsKey(responseFile))
                            throw;
                    }

                    LogManager.Write("Capture error: " + ex.Message);
                    Complete(commandFile, responseFile, new
                    {
                        status = "error",
                        request_id = command.request_id,
                        document_number = command.document_number,
                        message = ex.Message
                    });
                }
                finally
                {
                    CaptureState.End();
                }

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool TryReadCommand(string commandFile, out CaptureCommand command)
        {
            command = null;
            try
            {
                // A notification or a stable size alone does not mean the writer has closed it.
                using (var stream = new FileStream(commandFile, FileMode.Open,
                    FileAccess.Read, FileShare.None))
                using (var reader = new StreamReader(stream))
                {
                    command = new JavaScriptSerializer().Deserialize<CaptureCommand>(reader.ReadToEnd());
                }

                return command != null
                    && !string.IsNullOrWhiteSpace(command.command)
                    && IsFileName(command.request_id)
                    && IsFileName(command.document_number);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private static bool IsFileName(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value != "." && value != ".."
                && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static void Complete(string commandFile, string responseFile, object response)
        {
            string json = new JavaScriptSerializer().Serialize(response);
            lock (CompletedResponses) { CompletedResponses[responseFile] = json; }
            PublishResponse(commandFile, responseFile, json);
        }

        private static void PublishResponse(string commandFile, string responseFile, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(responseFile));
            if (!File.Exists(responseFile))
            {
                string temporaryFile = responseFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporaryFile, json);
                    File.Move(temporaryFile, responseFile);
                    LogManager.Write("Response created: " + Path.GetFileName(responseFile));
                }
                finally
                {
                    if (File.Exists(temporaryFile))
                        File.Delete(temporaryFile);
                }
            }

            File.Delete(commandFile);
        }
    }
}
