using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Localization;
using UnityEngine;

namespace ProfitBasedIndustryAndOffice
{
    [FileLocation(nameof(ProfitBasedIndustryAndOffice))]
    [SettingsUIGroupOrder(kLoggingGroup)]
    [SettingsUIShowGroupName(kLoggingGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kLoggingGroup = "Logging";

        private bool m_EnableVerboseLogging;

        [SettingsUISection(kSection, kLoggingGroup)]
        public bool EnableVerboseLogging
        {
            get => m_EnableVerboseLogging;
            set
            {
                if (m_EnableVerboseLogging == value)
                {
                    return;
                }

                m_EnableVerboseLogging = value;
                ModLog.VerboseEnabled = value;

                if (value)
                {
                    Debug.Log("[ProfitBasedIndustryAndOffice] Verbose logging enabled. This may impact performance.");
                }
            }
        }

        public Setting(IMod mod) : base(mod)
        {
        }

        public override void SetDefaults()
        {
            EnableVerboseLogging = false;
        }
    }

    public sealed class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Profit Based Industry and Office" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kLoggingGroup), "Logging" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableVerboseLogging)), "Enable verbose logging" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableVerboseLogging)), "Toggle detailed diagnostics output. Leave disabled during normal play to avoid slowdowns." }
            };
        }

        public void Unload()
        {
        }
    }
}