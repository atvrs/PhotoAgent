using System;
using System.IO;
using System.Web.Script.Serialization;

namespace PhotoAgent
{
    public static class CommandProcessor
    {
        private static readonly string ResponsesFolder =
            @"C:\psa_photos\responses";

        public static void Process(
            string commandFile)
        {
            if (CaptureState.IsBusy)
            {
                CreateBusyResponse(
                    commandFile
                );

                return;
            }

            CaptureState.IsBusy = true;
            CaptureState.Started = DateTime.Now;

            try
            {
                string json =
                    File.ReadAllText(
                        commandFile
                    );

                JavaScriptSerializer serializer =
                    new JavaScriptSerializer();

                CaptureCommand cmd =
                    serializer.Deserialize<CaptureCommand>(
                        json
                    );

                LogManager.Write(
                    "Request received: "
                    + cmd.request_id
                );

                LogManager.Write(
                    "Command: "
                    + cmd.command
                );

                LogManager.Write(
                    "Document: "
                    + cmd.document_number
                );

                CaptureResult result =
                    new CaptureResult();

                if (cmd.command ==
                    "capture_document")
                {
                    result =
                        CaptureWorkflow
                            .CaptureDocument(
                                cmd
                            );
                }
                else if (cmd.command ==
                    "capture_extra")
                {
                    result =
                        CaptureWorkflow
                            .CaptureExtra(
                                cmd
                            );
                }
                else
                {
                    LogManager.Write(
                        "Unknown command: "
                        + cmd.command
                    );
                }

                if (result.Success)
                {
                    CreateSuccessResponse(
                        cmd,
                        result
                    );
                }

                File.Delete(
                    commandFile
                );
            }
            catch (Exception ex)
            {
                LogManager.Write(
                    "Process error: "
                    + ex.Message
                );
            }
            finally
            {
                CaptureState.IsBusy = false;
            }
        }

        private static void CreateSuccessResponse(
            CaptureCommand cmd,
            CaptureResult result)
        {
            JavaScriptSerializer serializer =
                new JavaScriptSerializer();

            string responseJson =
                serializer.Serialize(
                    new
                    {
                        status = "ok",

                        request_id =
                            cmd.request_id,

                        document_number =
                            cmd.document_number,

                        files =
                            result.Files
                    }
                );

            string fileName =
                cmd.request_id
                + ".done";

            string fullPath =
                Path.Combine(
                    ResponsesFolder,
                    fileName
                );

            File.WriteAllText(
                fullPath,
                responseJson
            );

            LogManager.Write(
                "Response created: "
                + fileName
            );
        }

        private static void CreateBusyResponse(
            string commandFile)
        {
            try
            {
                LogManager.Write(
                    "CreateBusyResponse start"
                );

                string json =
                    File.ReadAllText(
                        commandFile
                    );

                JavaScriptSerializer serializer =
                    new JavaScriptSerializer();

                CaptureCommand cmd =
                    serializer.Deserialize<CaptureCommand>(
                        json
                    );

                string responseJson =
                    serializer.Serialize(
                        new
                        {
                            status = "busy",

                            request_id =
                                cmd.request_id,

                            document_number =
                                cmd.document_number,

                            message =
                                "PhotoAgent is busy"
                        }
                    );

                string responseFile =
                    Path.Combine(
                        ResponsesFolder,
                        cmd.request_id
                        + ".done"
                    );

                File.WriteAllText(
                    responseFile,
                    responseJson
                );

                File.Delete(
                    commandFile
                );

                LogManager.Write(
                    "BUSY: "
                    + cmd.request_id
                );
            }
            catch (Exception ex)
            {
                LogManager.Write(
                    "CreateBusyResponse error: "
                    + ex.Message
                );
            }
        }
    }
}