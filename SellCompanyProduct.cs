using System;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Game.Buildings;
using Game.Agents;
using Game.Citizens;
using Game.Net;
using Game.Pathfind;
using UnityEngine.Scripting;
using Colossal.Logging;
using Game.Tools;
using Game;
using Unity.Burst.Intrinsics;
using static Game.Prefabs.TriggerPrefabData;

namespace ProfitBasedIndustryAndOffice
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ResourceExporterSystem))]
    public partial class SellCompanyProductSystem : GameSystemBase
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ProfitBasedIndustryAndOffice)}.Combined.{nameof(SellCompanyProductSystem)}").SetShowsErrorsInUI(false);
        private EntityQuery m_CompanyQuery;
        private EntityQuery m_EconomyParameterQuery;
        private ResourceSystem m_ResourceSystem;
        private EndFrameBarrier m_EndFrameBarrier;
        private SimulationSystem m_SimulationSystem;
        private NativeQueue<VirtualExportEvent> m_ExportQueue;

        private const int kUpdatesPerDay = 32;
        private const int MINIMAL_EXPORT_AMOUNT = 200;

        private struct VirtualExportEvent
        {
            public Resource m_Resource;
            public Entity m_Seller;
            public int m_Amount;
        }

        private struct VirtualExportJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public BufferTypeHandle<Resources> ResourcesType;
            [ReadOnly] public ComponentTypeHandle<PrefabRef> PrefabRefType;
            [ReadOnly] public ComponentLookup<IndustrialProcessData> IndustrialProcessDatas;
            [ReadOnly] public ResourcePrefabs ResourcePrefabs;
            [ReadOnly] public ComponentLookup<ResourceData> ResourceDatas;
            public NativeQueue<VirtualExportEvent>.ParallelWriter ExportQueue;
            public EntityCommandBuffer.ParallelWriter CommandBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                try
                {
                    //log.Info($"VirtualExportJob: Starting processing for chunk {unfilteredChunkIndex}");

                    var entities = chunk.GetNativeArray(EntityType);
                    var resourceBuffers = chunk.GetBufferAccessor(ref ResourcesType);
                    var prefabRefs = chunk.GetNativeArray(ref PrefabRefType);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var entity = entities[i];
                        var resources = resourceBuffers[i];
                        var prefabRef = prefabRefs[i];

                        if (!IndustrialProcessDatas.HasComponent(prefabRef.m_Prefab))
                            continue;

                        var processData = IndustrialProcessDatas[prefabRef.m_Prefab];
                        var outputResource = processData.m_Output.m_Resource;

                        // Check if the output resource has a weight of 0 (office product)
                        if (ResourceDatas[ResourcePrefabs[outputResource]].m_Weight != 0)
                            continue;

                        var input1Resource = processData.m_Input1.m_Resource;
                        var input2Resource = processData.m_Input2.m_Resource;

                        //log.Info($"Processing entity {entity.Index} in chunk {unfilteredChunkIndex}");

                        // Check if the company has negative money
                        bool hasNegativeMoney = false;
                        for (int j = 0; j < resources.Length; j++)
                        {
                            if (resources[j].m_Resource == Resource.Money) {
                                if (resources[j].m_Amount < 0)
                                {
                                    hasNegativeMoney = true;
                                }
                                break;
                            }
                        }

                        for (int j = 0; j < resources.Length; j++)
                        {
                            var resource = resources[j];
                            int totalAmount = resource.m_Amount;
                            int exportAmount = totalAmount;

                            if (
                                (
                                    resource.m_Resource == outputResource
                                ) || (
                                    hasNegativeMoney &&
                                    (resource.m_Resource == input1Resource ||
                                    (input2Resource != Resource.NoResource && resource.m_Resource == input2Resource))
                                )
                            ){
                                //log.Info($"Entity {entity.Index}: Resource {resource.m_Resource}, Total: {totalAmount}, Buffer: {bufferAmount}, Export: {exportAmount}");

                                if (exportAmount > MINIMAL_EXPORT_AMOUNT)
                                {
                                    //log.Info($"Enqueueing export event for entity {entity.Index}: resource: {resource.m_Resource}, amount: {exportAmount}");
                                    ExportQueue.Enqueue(new VirtualExportEvent
                                    {
                                        m_Seller = entity,
                                        m_Amount = math.min(exportAmount, 1000),
                                        m_Resource = resource.m_Resource
                                    });
                                }
                            }
                        }
                    }

                    //log.Info($"VirtualExportJob: Finished processing for chunk {unfilteredChunkIndex}");
                }
                catch (Exception e)
                {
                    log.Error($"Exception in VirtualExportJob for chunk {unfilteredChunkIndex}: {e.Message}\n{e.StackTrace}");
                }
            }
        }

        private struct HandleVirtualExportsJob : IJob
        {
            public BufferLookup<Resources> Resources;
            [ReadOnly] public ComponentLookup<ResourceData> ResourceDatas;
            [ReadOnly] public ResourcePrefabs ResourcePrefabs;
            public NativeQueue<VirtualExportEvent> ExportQueue;

            public void Execute()
            {
                //log.Info("Starting HandleVirtualExportsJob");
                int processedEvents = 0;

                while (ExportQueue.TryDequeue(out VirtualExportEvent item))
                {
                    processedEvents++;
                    //log.Info($"Processing export event {processedEvents}: Resource {item.m_Resource}, Amount {item.m_Amount}");

                    if (Resources.HasBuffer(item.m_Seller))
                    {
                        var resourceBuffer = Resources[item.m_Seller];
                        int price = UnityEngine.Mathf.RoundToInt(EconomyUtils.GetMarketPrice(item.m_Resource, ResourcePrefabs, ref ResourceDatas) * item.m_Amount);

                        //log.Info($"Exporting resource: {item.m_Resource}, Amount: {item.m_Amount}, Price: {price}");

                        EconomyUtils.AddResources(item.m_Resource, -item.m_Amount, resourceBuffer);
                        EconomyUtils.AddResources(Resource.Money, price, resourceBuffer);

                        //log.Info($"Export completed for entity {item.m_Seller.Index}");
                    }
                    else
                    {
                        log.Info($"[WARNING] Entity {item.m_Seller.Index} does not have a resource buffer");
                    }
                }

                //log.Info($"HandleVirtualExportsJob completed. Processed {processedEvents} events.");
            }
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            log.Info("OnCreate: Initializing SellCompanyProductSystem");

            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_ExportQueue = new NativeQueue<VirtualExportEvent>(Allocator.Persistent);

            m_CompanyQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(),
                ComponentType.ReadWrite<WorkProvider>(),
                ComponentType.ReadOnly<UpdateFrame>(),
                ComponentType.ReadOnly<Resources>(),
                ComponentType.Exclude<ServiceAvailable>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            );

            m_EconomyParameterQuery = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());

            RequireForUpdate(m_CompanyQuery);
            RequireForUpdate(m_EconomyParameterQuery);

            log.Info("OnCreate: SellCompanyProductSystem initialized successfully");
        }

        [Preserve]
        protected override void OnDestroy()
        {
            log.Info("OnDestroy: Cleaning up SellCompanyProductSystem");
            base.OnDestroy();
            m_ExportQueue.Dispose();
            log.Info("OnDestroy: SellCompanyProductSystem cleaned up successfully");
        }

        [Preserve]
        protected override void OnUpdate()
        {
            try
            {
                log.Info("OnUpdate: Starting update cycle");

                if (m_CompanyQuery.IsEmptyIgnoreFilter)
                {
                    log.Info("No entities match the query. Skipping update.");
                    return;
                }

                uint updateFrame = SimulationUtils.GetUpdateFrame(m_SimulationSystem.frameIndex, kUpdatesPerDay, 16);
                log.Info($"Current update frame: {updateFrame}");

                var virtualExportJob = new VirtualExportJob
                {
                    EntityType = GetEntityTypeHandle(),
                    ResourcesType = GetBufferTypeHandle<Resources>(true),
                    PrefabRefType = GetComponentTypeHandle<PrefabRef>(true),
                    IndustrialProcessDatas = GetComponentLookup<IndustrialProcessData>(true),
                    ResourcePrefabs = m_ResourceSystem.GetPrefabs(),
                    ResourceDatas = GetComponentLookup<ResourceData>(true),
                    ExportQueue = m_ExportQueue.AsParallelWriter()
                };

                log.Info("Scheduling VirtualExportJob");
                Dependency = virtualExportJob.ScheduleParallel(m_CompanyQuery, Dependency);

                var handleVirtualExportsJob = new HandleVirtualExportsJob
                {
                    Resources = GetBufferLookup<Resources>(),
                    ResourceDatas = GetComponentLookup<ResourceData>(true),
                    ResourcePrefabs = m_ResourceSystem.GetPrefabs(),
                    ExportQueue = m_ExportQueue
                };

                log.Info("Scheduling HandleVirtualExportsJob");
                Dependency = handleVirtualExportsJob.Schedule(Dependency);

                log.Info("Adding job handles to EndFrameBarrier");
                m_EndFrameBarrier.AddJobHandleForProducer(Dependency);

                log.Info("OnUpdate completed successfully");
            }
            catch (Exception e)
            {
                log.Error($"Exception in OnUpdate: {e.Message}\n{e.StackTrace}");
            }
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 262144 / (kUpdatesPerDay * 16);
        }
    }
}