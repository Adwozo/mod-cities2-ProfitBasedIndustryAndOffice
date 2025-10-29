using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using System;
using ProfitBasedIndustryAndOffice.Prefabs;

namespace ProfitBasedIndustryAndOffice.ModSystem
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ProfitBasedIndustryAndOffice)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
            }

            CompanyFinancialsManager.Initialize(1000000);
            RegisterModifiedSystem(updateSystem);
        }

        private void RegisterModifiedSystem(UpdateSystem updateSystem)
        {
            try
            {
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

            CompanyFinancialsManager.Dispose();
            log.Info("CompanyFinancialsManager disposed");
        }
    }
}