using Game;
using Unity.Entities;
using Game.Simulation;
using Game.Companies;
using Game.Buildings;
using Game.Economy;
using Game.Prefabs;
using Game.Vehicles;
using UnityEngine;
using Unity.Collections;
using Unity.Burst.Intrinsics;
using Colossal.Logging;
using Unity.Mathematics;
using System;
using ProfitBasedIndustryAndOffice.Prefabs;

namespace ProfitBasedIndustryAndOffice.ModSystem
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(IndustrialAISystem))]
    public partial class ModifiedIndustrialAISystem : GameSystemBase
    {
        private ResourceSystem m_ResourceSystem;
        private EndFrameBarrier m_EndFrameBarrier;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_CompanyQuery;

        public static ILog log = LogManager.GetLogger($"{nameof(ProfitBasedIndustryAndOffice)}.Combined.{nameof(ModifiedIndustrialAISystem)}").SetShowsErrorsInUI(false);

        private const int HEADCOUNT_TICK_CHANGE = 2;
        private const int kUpdatesPerDay = 32;

        private struct CompanyAITickJob : IJobChunk
        {
            public EntityTypeHandle EntityType;
            public ComponentTypeHandle<WorkProvider> WorkProviderType;
            [ReadOnly] public BufferTypeHandle<Game.Economy.Resources> ResourceType;
            [ReadOnly] public BufferTypeHandle<OwnedVehicle> VehicleType;
            [ReadOnly] public BufferTypeHandle<TradeCost> TradeCostType;
            [ReadOnly] public BufferTypeHandle<Employee> EmployeeType;
            [ReadOnly] public ComponentLookup<PropertyRenter> PropertyRenters;
            [ReadOnly] public ComponentLookup<PrefabRef> Prefabs;
            [ReadOnly] public ComponentLookup<OfficeBuilding> OfficeBuildings;
            [ReadOnly] public ComponentLookup<BuildingData> BuildingDatas;
            [ReadOnly] public ComponentLookup<BuildingPropertyData> BuildingPropertyDatas;
            [ReadOnly] public ComponentLookup<SpawnableBuildingData> SpawnableBuildingDatas;
            [ReadOnly] public ComponentLookup<IndustrialProcessData> IndustrialProcessDatas;
            [ReadOnly] public BufferLookup<LayoutElement> Layouts;
            [ReadOnly] public ComponentLookup<Game.Vehicles.DeliveryTruck> Trucks;
            [ReadOnly] public ComponentLookup<ResourceData> ResourceDatas;
            [ReadOnly] public SharedComponentTypeHandle<UpdateFrame> UpdateFrameType;

            public ResourcePrefabs ResourcePrefabs;
            public uint UpdateFrameIndex;
            public NativeParallelHashMap<Entity, CompanyFinancials> CompanyFinancialsMap;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.GetSharedComponent(UpdateFrameType).m_Index != UpdateFrameIndex)
                {
                    return;
                }

                NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                NativeArray<WorkProvider> workProviders = chunk.GetNativeArray(ref WorkProviderType);
                BufferAccessor<Game.Economy.Resources> resources = chunk.GetBufferAccessor(ref ResourceType);
                BufferAccessor<OwnedVehicle> vehicles = chunk.GetBufferAccessor(ref VehicleType);
                BufferAccessor<TradeCost> tradeCosts = chunk.GetBufferAccessor(ref TradeCostType);
                BufferAccessor<Employee> employees = chunk.GetBufferAccessor(ref EmployeeType);

                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = entities[i];
                    WorkProvider workProvider = workProviders[i];

                    if (!PropertyRenters.HasComponent(entity)) continue;

                    Entity property = PropertyRenters[entity].m_Property;

                    int companyMoney = EconomyUtils.GetResources(Resource.Money, resources[i]);

                    Entity prefab = Prefabs[entity].m_Prefab;
                    var processData = IndustrialProcessDatas[prefab];

                    float materialCost = 0f;

                    materialCost += EconomyUtils.GetTradeCost(processData.m_Input1.m_Resource, tradeCosts[i]).m_BuyCost * processData.m_Input1.m_Amount;
                    if (processData.m_Input2.m_Resource != Resource.NoResource)
                    {
                        materialCost += EconomyUtils.GetTradeCost(processData.m_Input2.m_Resource, tradeCosts[i]).m_BuyCost * processData.m_Input2.m_Amount;
                    }

                    float companyFreeCash = companyMoney - materialCost;

                    Entity prefab2 = Prefabs[property].m_Prefab;
                    int fittingWorkers = GetFittingWorkers(
                        BuildingDatas[prefab2],
                        BuildingPropertyDatas[prefab2],
                        SpawnableBuildingDatas[prefab2].m_Level,
                        processData
                    );

                    /**string logMessage = $"Company: {entity.Index} | Max Workers: {workProvider.m_MaxWorkers} | " +
                                        $"Fitting Workers: {fittingWorkers} | Company Money: {companyMoney} | " +
                                        $"Material Cost: {materialCost:F2} | Profit: {profit:F2} | " +
                                        $"Company Worth: {companyWorth:F2} | Profit-to-Worth Ratio: {profitToWorthRatio:F2} | " +
                                        $"Expansion Threshold: {EXPANSION_THRESHOLD:F2} | Contraction Threshold: {CONTRACTION_THRESHOLD:F2}";

                    log.Info(logMessage);**/
                    int minimumWorkers = math.max(fittingWorkers / 4, 1);

                    CompanyFinancials financials = default;
                    bool hasEntry = false;
                    if (CompanyFinancialsMap.IsCreated)
                    {
                        hasEntry = CompanyFinancialsMap.TryGetValue(entity, out financials);
                    }

                    bool shouldAddEntry = !hasEntry;

                    if (!hasEntry || !financials.Initialized)
                    {
                        financials = new CompanyFinancials
                        {
                            LastCashBalance = companyMoney,
                            HeadCount = math.max(workProvider.m_MaxWorkers, minimumWorkers),
                            Initialized = true
                        };
                    }
                    else if (financials.HeadCount == 0)
                    {
                        financials.HeadCount = math.max(workProvider.m_MaxWorkers, minimumWorkers);
                    }

                    workProvider.m_MaxWorkers = math.max(financials.HeadCount, minimumWorkers);

                    int profit = companyMoney - financials.LastCashBalance;
                    bool shouldIncrease = false;
                    bool shouldDecrease = false;

                    if (profit > 0)
                    {
                        shouldIncrease = true;
                    }
                    else if (profit < 0)
                    {
                        float lossThreshold = math.max(math.abs(financials.LastCashBalance), 1) * 0.1f;
                        if (math.abs(profit) > lossThreshold)
                        {
                            shouldDecrease = true;
                        }
                    }
                    else
                    {
                        if (companyFreeCash > 0)
                        {
                            shouldIncrease = true;
                        }
                        else if (companyFreeCash < 0)
                        {
                            shouldDecrease = true;
                        }
                    }

                    if (shouldIncrease && workProvider.m_MaxWorkers < fittingWorkers)
                    {
                        workProvider.m_MaxWorkers = math.min(workProvider.m_MaxWorkers + HEADCOUNT_TICK_CHANGE, fittingWorkers);
                    }
                    else if (shouldDecrease && workProvider.m_MaxWorkers > minimumWorkers)
                    {
                        workProvider.m_MaxWorkers = math.max(workProvider.m_MaxWorkers - HEADCOUNT_TICK_CHANGE, minimumWorkers);
                    }

                    if (workProvider.m_MaxWorkers < minimumWorkers)
                    {
                        workProvider.m_MaxWorkers = minimumWorkers;
                    }

                    financials.HeadCount = workProvider.m_MaxWorkers;
                    financials.LastCashBalance = companyMoney;

                    if (CompanyFinancialsMap.IsCreated)
                    {
                        if (shouldAddEntry)
                        {
                            CompanyFinancialsMap.TryAdd(entity, financials);
                        }
                        else
                        {
                            CompanyFinancialsMap[entity] = financials;
                        }
                    }

                    workProviders[i] = workProvider;
                }
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_CompanyQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(),
                ComponentType.ReadWrite<WorkProvider>(),
                ComponentType.ReadOnly<UpdateFrame>(),
                ComponentType.Exclude<ServiceAvailable>()
                );
            log.Info("ModifiedIndustrialAISystem created");
        }

        protected override void OnUpdate()
        {
            try
            {
                log.Info("OnUpdate: Starting update cycle");

                uint updateFrame = SimulationUtils.GetUpdateFrame(m_SimulationSystem.frameIndex, kUpdatesPerDay, 16);
                log.Info($"Current update frame: {updateFrame}");

                log.Info("Scheduling CompanyAITickJob");
                CompanyAITickJob job = new CompanyAITickJob
                {
                    EntityType = GetEntityTypeHandle(),
                    WorkProviderType = GetComponentTypeHandle<WorkProvider>(),
                    ResourceType = GetBufferTypeHandle<Game.Economy.Resources>(true),
                    VehicleType = GetBufferTypeHandle<OwnedVehicle>(true),
                    TradeCostType = GetBufferTypeHandle<TradeCost>(true),
                    EmployeeType = GetBufferTypeHandle<Employee>(true),
                    PropertyRenters = GetComponentLookup<PropertyRenter>(true),
                    Prefabs = GetComponentLookup<PrefabRef>(true),
                    OfficeBuildings = GetComponentLookup<OfficeBuilding>(true),
                    BuildingDatas = GetComponentLookup<BuildingData>(true),
                    BuildingPropertyDatas = GetComponentLookup<BuildingPropertyData>(true),
                    SpawnableBuildingDatas = GetComponentLookup<SpawnableBuildingData>(true),
                    IndustrialProcessDatas = GetComponentLookup<IndustrialProcessData>(true),
                    Layouts = GetBufferLookup<LayoutElement>(true),
                    Trucks = GetComponentLookup<Game.Vehicles.DeliveryTruck>(true),
                    ResourceDatas = GetComponentLookup<ResourceData>(true),
                    UpdateFrameType = GetSharedComponentTypeHandle<UpdateFrame>(),
                    ResourcePrefabs = m_ResourceSystem.GetPrefabs(),
                    UpdateFrameIndex = updateFrame,
                    CompanyFinancialsMap = CompanyFinancialsManager.GetCompanyFinancialsMap()
                };

                Dependency = job.Schedule(m_CompanyQuery, Dependency);

                log.Info("Adding job handles to EndFrameBarrier");
                m_EndFrameBarrier.AddJobHandleForProducer(Dependency);

                log.Info("OnUpdate completed successfully");
            }
            catch (Exception e)
            {
                log.Info($"Exception in OnUpdate: {e.Message}\n{e.StackTrace}");
            }
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 262144 / (kUpdatesPerDay * 16);
        }

        private static int GetFittingWorkers(BuildingData building, BuildingPropertyData properties, int level, IndustrialProcessData processData)
        {
            return Mathf.CeilToInt(processData.m_MaxWorkersPerCell * (float)building.m_LotSize.x * (float)building.m_LotSize.y * (1f + 0.5f * (float)level) * properties.m_SpaceMultiplier);
        }
    }
}