using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Unity.Entities;
using System;
using ProfitBasedIndustryAndOffice.Prefabs;

namespace ProfitBasedIndustryAndOffice.ModSystem
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ProfitBasedIndustryAndOffice)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        private Setting m_Setting;
        private World m_SimulationWorld;
        private SystemHandle m_SellCompanyProductSystemHandle;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));

            AssetDatabase.global.LoadSettings(nameof(ProfitBasedIndustryAndOffice), m_Setting, new Setting(this));

            // Get the simulation world
            m_SimulationWorld = World.DefaultGameObjectInjectionWorld;

            // Register systems
            CompanyFinancialsManager.Initialize(1000000);
            RegisterModifiedSystem(updateSystem);
            RegisterSellCompanyProductSystem(updateSystem);
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

        private void RegisterSellCompanyProductSystem(UpdateSystem updateSystem)
        {
            try
            {
                // Create and add the SellCompanyProductSystem to the simulation world
                m_SellCompanyProductSystemHandle = m_SimulationWorld.CreateSystem<SellCompanyProductSystem>();

                // Register the system with the update system
                updateSystem.UpdateAt<SellCompanyProductSystem>(SystemUpdatePhase.GameSimulation);

                log.Info("SellCompanyProductSystem added to simulation world");
            }
            catch (Exception e)
            {
                log.Error($"Failed to register SellCompanyProductSystem: {e.Message}");
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

            // Remove the SellCompanyProductSystem from the simulation world
            if (m_SimulationWorld != null && m_SellCompanyProductSystemHandle != default)
            {
                m_SimulationWorld.DestroySystem(m_SellCompanyProductSystemHandle);
                m_SellCompanyProductSystemHandle = default;
                log.Info("SellCompanyProductSystem removed from simulation world");
            }

            CompanyFinancialsManager.Dispose();
            log.Info("CompanyFinancialsManager disposed");

            // Cleanup for ModifiedIndustrialAISystem is not needed here as the game will handle system disposal
        }
    }
}