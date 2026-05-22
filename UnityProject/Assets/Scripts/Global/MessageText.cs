using System;
using XNClient.Logger;

public static class MessageText
{
    public static string Get(string id)
    {
        if (string.IsNullOrEmpty(id)) {
            return string.Empty;
        }

        MessageDataType data = MessageDataType.GetConfigData(id);
        if (data == null) {
            XNLogger.LogWarn("Message config not found.", ("id", id));
            return id;
        }

        return data.text ?? string.Empty;
    }

    public static string Format(string id, params object[] args)
    {
        string template = Get(id);
        if (args == null || args.Length == 0) {
            return template;
        }

        try {
            return string.Format(template, args);
        }
        catch (FormatException e) {
            XNLogger.LogError("Message config format failed.", ("id", id), ("error", e.Message));
            return template;
        }
    }
}
