using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;
using System.Collections.Generic;

namespace ProfitBasedIndustryAndOffice
{
    [FileLocation(nameof(ProfitBasedIndustryAndOffice))]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";

        public Setting(IMod mod) : base(mod) { }
        public override void SetDefaults() { }
    }

    public class LocaleEN : IDictionarySource
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
                { m_Setting.GetSettingsLocaleID(), "PBIAO (WIP)" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
            };
        }

        public void Unload()
        {

        }
    }
}