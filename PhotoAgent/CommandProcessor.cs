using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace PhotoAgent
{
    public static class CommandProcessor
    {
        private const string ResponsesFolder = @"C:\psa_photos\responses";
        private sealed class CompletedRequest
        {
            internal string Json;
            internal PhotoTransfer Transfer;
        }
        private static readonly Dictionary<string, CompletedRequest> CompletedResponses =
            new Dictionary<string, CompletedRequest>(StringComparer.OrdinalIgnoreCase);

        public static bool Process(string commandFile)
        {
            return Process(commandFile, ResponsesFolder);
        }

        internal static bool Process(string commandFile, string responsesFolder)
        {
            return Process(commandFile, responsesFolder, PhotoTransfer.DataRoot);
        }

        internal static bool Process(string commandFile, string responsesFolder, string dataRoot)
        {
            CaptureCommand command;
            if (!TryReadCommand(commandFile, out command))
                return false;

            string responseFile = Path.Combine(responsesFolder, command.request_id + ".done");
            try
            {
                if (File.Exists(TransferCache.ExpiredNotice(dataRoot, command.request_id)))
                {
                    PublishResponse(commandFile, responseFile, new JavaScriptSerializer().Serialize(new
                    {
                        status = "error",
                        code = "transfer_cache_expired",
                        request_id = command.request_id,
                        document_number = command.document_number,
                        message = "Временные файлы задания уже удалены из transfer. Для новой съёмки нужен новый request_id.",
                        photo_count = 0,
                        photos = new ReadyPhoto[0]
                    }));
                    return true;
                }

                CompletedRequest completedResponse;
                lock (CompletedResponses)
                {
                    CompletedResponses.TryGetValue(responseFile, out completedResponse);
                }

                if (completedResponse != null)
                {
                    PublishCompleted(commandFile, responseFile, completedResponse);
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

                using (var transfer = new PhotoTransfer(command, responsesFolder, dataRoot))
                {
                    try
                    {
                        if (transfer.RecoverInterrupted())
                            throw new InvalidOperationException(
                                "Обработка задания прервана перезапуском PhotoAgent. Для новой съёмки используйте новый request_id.");

                        LogManager.Write("Request received: " + command.request_id
                            + "; command: " + command.command);

                        CaptureResult result;
                        if (command.command == "capture_document")
                            result = CaptureWorkflow.CaptureDocument(command, transfer.PhotoSaved);
                        else if (command.command == "capture_extra")
                            result = CaptureWorkflow.CaptureExtra(command, transfer.PhotoSaved);
                        else
                            throw new InvalidOperationException("Unknown command: " + command.command);

                        if (CaptureState.TimedOut)
                            throw new TimeoutException("Capture timeout");
                        if (!result.Success)
                            throw new InvalidOperationException(result.ErrorMessage
                                ?? "Некорректное завершение обработки задания");

                        Complete(commandFile, responseFile, new
                        {
                            status = "ok",
                            request_id = command.request_id,
                            document_number = command.document_number,
                            files = result.Files,
                            photo_count = transfer.Photos.Count,
                            photos = transfer.Photos
                        }, transfer);
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
                            message = ex.Message,
                            photo_count = transfer.Photos.Count,
                            photos = transfer.Photos
                        }, transfer);
                    }
                    finally
                    {
                        CaptureState.End();
                    }
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

        private static void Complete(string commandFile, string responseFile, object response,
            PhotoTransfer transfer)
        {
            var completed = new CompletedRequest
            {
                Json = new JavaScriptSerializer().Serialize(response),
                Transfer = transfer
            };
            lock (CompletedResponses) { CompletedResponses[responseFile] = completed; }
            PublishCompleted(commandFile, responseFile, completed);
        }

        private static void PublishCompleted(string commandFile, string responseFile,
            CompletedRequest completed)
        {
            // The terminal response must not overtake any pending photo notice.
            completed.Transfer.Flush();
            PublishResponse(commandFile, responseFile, completed.Json);
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
