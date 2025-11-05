using System;
using Colossal.Logging;
using UnityEngine;

namespace ProfitBasedIndustryAndOffice
{
    internal static class ModLog
    {
        private static bool s_VerboseEnabled;

        public static bool VerboseEnabled
        {
            get => s_VerboseEnabled;
            set => s_VerboseEnabled = value;
        }

        public static void Info(ILog logger, string message)
        {
            Write(logger, l => l.Info(message), message, always: false);
        }

        public static void Warn(ILog logger, string message)
        {
            Write(logger, l => l.Warn(message), message, always: false);
        }

        public static void Error(ILog logger, string message)
        {
            Write(logger, l => l.Error(message), message, always: true);
        }

        private static void Write(ILog logger, Action<ILog> action, string fallbackMessage, bool always)
        {
            if (!always && !s_VerboseEnabled)
            {
                return;
            }

            if (logger == null)
            {
                if (always && !string.IsNullOrWhiteSpace(fallbackMessage))
                {
                    Debug.LogError(fallbackMessage);
                }

                return;
            }

            try
            {
                action(logger);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"ProfitBasedIndustryAndOffice logger failure: {ex.Message}");
                if (!always && s_VerboseEnabled)
                {
                    s_VerboseEnabled = false;
                    Debug.LogWarning("Verbose logging disabled to avoid repeated logger failures.");
                }

                if (always && !string.IsNullOrWhiteSpace(fallbackMessage))
                {
                    Debug.LogError(fallbackMessage);
                }
            }
        }
    }
}
