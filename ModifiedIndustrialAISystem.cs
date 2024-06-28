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
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Colossal.Logging;
using Unity.Mathematics;

namespace ProfitBasedIndustryAndOffice
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(IndustrialAISystem))]
    public partial class ModifiedIndustrialAISystem : GameSystemBase
    {
        private ResourceSystem m_ResourceSystem;
        private EndFrameBarrier m_EndFrameBarrier;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_CompanyQuery;

        public static ILog Log => Mod.log;

        private const float EXPANSION_THRESHOLD = 0.0f;
        private const float CONTRACTION_THRESHOLD = -0.5f;
        private const int kUpdatesPerDay = 32;

        [BurstCompile]
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
            public EntityCommandBuffer.ParallelWriter CommandBuffer;

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
                    bool isOffice = OfficeBuildings.HasComponent(property);

                    float companyWorth = EconomyUtils.GetCompanyTotalWorth(resources[i], vehicles[i], Layouts, Trucks, ResourcePrefabs, ResourceDatas);
                    int companyMoney = EconomyUtils.GetResources(Resource.Money, resources[i]);

                    Entity prefab = Prefabs[entity].m_Prefab;
                    var processData = IndustrialProcessDatas[prefab];

                    float materialCost = 0f;
                    if (!isOffice)
                    {
                        materialCost += EconomyUtils.GetTradeCost(processData.m_Input1.m_Resource, tradeCosts[i]).m_BuyCost * processData.m_Input1.m_Amount;
                        if (processData.m_Input2.m_Resource != Resource.NoResource)
                        {
                            materialCost += EconomyUtils.GetTradeCost(processData.m_Input2.m_Resource, tradeCosts[i]).m_BuyCost * processData.m_Input2.m_Amount;
                        }
                    }

                    float profit = companyMoney - materialCost;
                    float profitToWorthRatio = profit / companyWorth;

                    Entity prefab2 = Prefabs[property].m_Prefab;
                    int fittingWorkers = GetFittingWorkers(
                        BuildingDatas[prefab2],
                        BuildingPropertyDatas[prefab2],
                        SpawnableBuildingDatas[prefab2].m_Level,
                        processData
                    );

                    if (companyWorth < 1500000)
                    {
                        workProvider.m_MaxWorkers = fittingWorkers;
                    }
                    else if (profitToWorthRatio > EXPANSION_THRESHOLD && workProvider.m_MaxWorkers < fittingWorkers)
                    {
                        workProvider.m_MaxWorkers = math.min(workProvider.m_MaxWorkers + 3, fittingWorkers);
                    }
                    else if (profitToWorthRatio < CONTRACTION_THRESHOLD && workProvider.m_MaxWorkers > 15)
                    {
                        workProvider.m_MaxWorkers = math.max(workProvider.m_MaxWorkers - 3, 15);
                    }
                    if (workProvider.m_MaxWorkers < 15)
                    {
                        workProvider.m_MaxWorkers = 15;
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
            Log.Info("ModifiedIndustrialAISystem created");
        }

        protected override void OnUpdate()
        {
            uint updateFrame = SimulationUtils.GetUpdateFrame(m_SimulationSystem.frameIndex, kUpdatesPerDay, 16);

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
                CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter()
            };

            Dependency = job.ScheduleParallel(m_CompanyQuery, Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(Dependency);
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