using System;
using System.IO;
using Newtonsoft.Json;
using XNClient.Logger;

public sealed class LanRoomResumeTicket
{
    public string roomId;
    public string sessionId;
    public string resumeToken;
    public string hostAddress;
    public int tcpPort;
    public string boardCfgId;
    public string holdTimeCfgId;
    public string byoyomiCountCfgId;
    public string byoyomiTimeCfgId;
    public string handicapCfgId;
    public int hostPlayerFlag;
    public string hostPlayerSideCfgId;
}

public static class LanRoomResumeTicketStore
{
    private static string TicketPath => Path.Combine(GameSaveConfig.SaveRootPath, "LanRoomResumeTicket.json");

    public static bool TryLoad(out LanRoomResumeTicket ticket)
    {
        ticket = null;
        try {
            string path = TicketPath;
            if (!File.Exists(path)) {
                return false;
            }

            ticket = JsonConvert.DeserializeObject<LanRoomResumeTicket>(File.ReadAllText(path));
            return IsValid(ticket);
        }
        catch (Exception e) {
            XNLogger.LogWarn("Load LAN room resume ticket failed.", ("error", e.Message));
            ticket = null;
            return false;
        }
    }

    public static void Save(LanRoomResumeTicket ticket)
    {
        if (!IsValid(ticket)) {
            return;
        }

        try {
            string path = TicketPath;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(ticket, Formatting.None));
        }
        catch (Exception e) {
            XNLogger.LogWarn("Save LAN room resume ticket failed.", ("error", e.Message));
        }
    }

    public static void Clear()
    {
        try {
            string path = TicketPath;
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
        catch (Exception e) {
            XNLogger.LogWarn("Clear LAN room resume ticket failed.", ("error", e.Message));
        }
    }

    private static bool IsValid(LanRoomResumeTicket ticket)
    {
        return ticket != null
            && !string.IsNullOrEmpty(ticket.roomId)
            && !string.IsNullOrEmpty(ticket.sessionId)
            && !string.IsNullOrEmpty(ticket.resumeToken)
            && !string.IsNullOrEmpty(ticket.hostAddress)
            && ticket.tcpPort > 0;
    }
}
