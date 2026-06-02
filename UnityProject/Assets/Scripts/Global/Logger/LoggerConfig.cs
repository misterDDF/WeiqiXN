using System.IO;
using UnityEngine;

namespace XNClient.Logger
{
    public static class LoggerConfig
    {
        public readonly static string PATH_LOG = Path.Combine(Application.persistentDataPath, "log");
        public static bool ENABLE_LOG_WIRTER = true;
        public static bool ENABLE_EVENT_VERBOSE_LOG = false;
        public static bool ENABLE_FSM_VERBOSE_LOG = false;
        public static bool ENABLE_DUEL_AI_VERBOSE_LOG = false;
        public static bool ENABLE_DUEL_AI_DETAIL_LOG = false;
        public static bool ENABLE_OGS_VERBOSE_LOG = false;
    }
}
