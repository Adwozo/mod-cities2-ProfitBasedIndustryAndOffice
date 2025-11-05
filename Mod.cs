using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using System;
using ProfitBasedIndustryAndOffice.Prefabs;
using UnityEngine;

namespace ProfitBasedIndustryAndOffice.ModSystem
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ProfitBasedIndustryAndOffice)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
    private Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            ModLog.Info(log, nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                ModLog.Info(log, $"Current mod asset at {asset.path}");
            }

            m_Setting = new Setting(this);
            AssetDatabase.global.LoadSettings(nameof(ProfitBasedIndustryAndOffice), m_Setting, new Setting(this));
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));
            ModLog.VerboseEnabled = m_Setting.EnableVerboseLogging;

            CompanyFinancialsManager.Initialize(1000000);
            RegisterModifiedSystem(updateSystem);
        }

        private void RegisterModifiedSystem(UpdateSystem updateSystem)
        {
            try
            {
                updateSystem.UpdateAt<ModifiedIndustrialAISystem>(SystemUpdatePhase.GameSimulation);
                ModLog.Info(log, "ModifiedIndustrialAISystem registered successfully.");
            }
            catch (Exception e)
            {
                ModLog.Error(log, $"Failed to register ModifiedIndustrialAISystem: {e.Message}");
                Debug.LogException(e);
            }
        }

        public void OnDispose()
        {
            ModLog.Info(log, nameof(OnDispose));

            CompanyFinancialsManager.Dispose();
            ModLog.Info(log, "CompanyFinancialsManager disposed");

            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }

            ModLog.VerboseEnabled = false;
        }
    }
}