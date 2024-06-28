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
using Unity.Jobs;
using System;
using Game.Zones;
using Game.Common;

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
        private uint m_NextUpdateFrame;
        private int updatecount;
        public Boolean isRunning;


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
                    if (!PropertyRenters.HasComponent(entity)) continue;
                    BuildingPropertyData buildingPropertyData = BuildingPropertyDatas[entity];
                    //if (buildingPropertyData.CountProperties(AreaType.Industrial) <= 0) continue;
                    Entity property = PropertyRenters[entity].m_Property;

                    WorkProvider workProvider = workProviders[i];

                    float companyWorth = EconomyUtils.GetCompanyTotalWorth(resources[i], vehicles[i], Layouts, Trucks, ResourcePrefabs, ResourceDatas);
                    int companyMoney = EconomyUtils.GetResources(Resource.Money, resources[i]);

                    Entity prefab = Prefabs[entity].m_Prefab;
                    var processData = IndustrialProcessDatas[prefab];

                    float materialCost = 0f;

                    materialCost += EconomyUtils.GetTradeCost(processData.m_Input1.m_Resource, tradeCosts[i]).m_BuyCost * processData.m_Input1.m_Amount;
                    if (processData.m_Input2.m_Resource != Resource.NoResource)
                    {
                        materialCost += EconomyUtils.GetTradeCost(processData.m_Input2.m_Resource, tradeCosts[i]).m_BuyCost * processData.m_Input2.m_Amount;
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
            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            isRunning = false;
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            m_CompanyQuery = GetEntityQuery(
                ComponentType.Exclude<ServiceAvailable>(),
                ComponentType.ReadWrite<WorkProvider>(),
                ComponentType.ReadOnly<UpdateFrame>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Economy.Resources>(),
                ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(),
                ComponentType.ReadOnly<TradeCost>(),
                ComponentType.Exclude<Created>(),
                ComponentType.Exclude<Deleted>()
                );

            m_NextUpdateFrame = SimulationUtils.GetUpdateFrame(m_SimulationSystem.frameIndex, COMPANY_UPDATES_PER_DAY, COMPANY_UPDATE_GROUP_COUNT);
            Log.Info("ModifiedIndustrialAISystem created");
            Log.Info($"Next CompanyAITickJob, frame: {m_NextUpdateFrame}");
        }

        protected override void OnUpdate()
        {
            uint updateFrame = SimulationUtils.GetUpdateFrame(m_SimulationSystem.frameIndex, kUpdatesPerDay, 16);
            Log.Info($"frame: {updateFrame}");
            
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
                CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter()
            };
            CompanyAITickJob jobData = job;
            base.Dependency = JobChunkExtensions.ScheduleParallel(jobData, m_CompanyQuery, base.Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(base.Dependency);
            m_ResourceSystem.AddPrefabsReader(base.Dependency);
        }

        public const int COMPANY_UPDATES_PER_DAY = 32; // Adjust this as needed
        public const int COMPANY_UPDATE_GROUP_COUNT = 16; // This matches the game's constant

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 262144 / (COMPANY_UPDATES_PER_DAY * COMPANY_UPDATE_GROUP_COUNT);
        }

        private static int GetFittingWorkers(BuildingData building, BuildingPropertyData properties, int level, IndustrialProcessData processData)
        {
            return Mathf.CeilToInt(processData.m_MaxWorkersPerCell * (float)building.m_LotSize.x * (float)building.m_LotSize.y * (1f + 0.5f * (float)level) * properties.m_SpaceMultiplier);
        }
    }
}