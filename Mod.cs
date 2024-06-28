using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Unity.Entities;
using Game.Simulation;
using System;

namespace ProfitBasedIndustryAndOffice
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ProfitBasedIndustryAndOffice)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        private Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));

            AssetDatabase.global.LoadSettings(nameof(ProfitBasedIndustryAndOffice), m_Setting, new Setting(this));

            // Register your modified system
            RegisterModifiedSystem(updateSystem);
        }

        private void RegisterModifiedSystem(UpdateSystem updateSystem)
        {
            try
            {
                // Register the system to update in the GameSimulation phase
                updateSystem.UpdateAt<ModifiedIndustrialAISystem>(SystemUpdatePhase.GameSimulation);
                log.Info("ModifiedIndustrialAISystem registered successfully.");
            }
            catch (Exception e)
            {
                log.Error($"Failed to register ModifiedIndustrialAISystem: {e.Message}");
            }
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }

            // Cleanup for your system is not needed here as the game will handle system disposal
        }
    }
}