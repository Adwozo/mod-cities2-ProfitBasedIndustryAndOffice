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
using ProfitBasedIndustryAndOffice.Prefabs;

namespace ProfitBasedIndustryAndOffice.ModSystem
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ResourceExporterSystem))]
    [UpdateBefore(typeof(ResourceBuyerSystem))]
    public partial class SellCompanyProductSystem : GameSystemBase
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ProfitBasedIndustryAndOffice)}.Combined.{nameof(SellCompanyProductSystem)}").SetShowsErrorsInUI(false);
        private EntityQuery m_CompanyQuery;
        private EntityQuery m_EconomyParameterQuery;
        private ResourceSystem m_ResourceSystem;
        private EndFrameBarrier m_EndFrameBarrier;
        private SimulationSystem m_SimulationSystem;
        private NativeQueue<VirtualExportEvent> m_ExportQueue;
        private NativeQueue<ResourceNeed> m_ResourceNeedQueue;

        private const int kUpdatesPerDay = 32;
        private const int MINIMAL_EXPORT_AMOUNT = 20;
        private const int MINIMAL_STORAGE_AMOUNT = 20;

        private struct ResourceNeed
        {
            public Resource Resource;
            public Entity Company;
            public int Amount;
        }

        private struct VirtualExportEvent
        {
            public Resource m_Resource;
            public Entity m_Seller;
            public Entity m_Buyer;
            public int m_Amount;
            public int m_CurrentCashHolding;
        }

        private struct CreateResourceMapJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public BufferTypeHandle<Resources> ResourcesType;
            [ReadOnly] public ComponentTypeHandle<PrefabRef> PrefabRefType;
            [ReadOnly] public ComponentLookup<IndustrialProcessData> IndustrialProcessDatas;
            [ReadOnly] public ResourcePrefabs ResourcePrefabs;
            [ReadOnly] public ComponentLookup<ResourceData> ResourceDatas;

            public NativeQueue<ResourceNeed>.ParallelWriter ResourceNeedQueue;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
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

                    for (int j = 0; j < resources.Length; j++)
                    {
                        var resource = resources[j];
                        if (resource.m_Resource == input1Resource ||
                            (input2Resource != Resource.NoResource && resource.m_Resource == input2Resource))
                        {
                            int amountNeeded = processData.m_Input1.m_Amount - resource.m_Amount;
                            if (amountNeeded > 0)
                            {
                                ResourceNeedQueue.Enqueue(new ResourceNeed
                                {
                                    Resource = resource.m_Resource,
                                    Company = entity,
                                    Amount = amountNeeded
                                });
                            }
                        }
                    }
                }
            }
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
            [ReadOnly] public NativeArray<ResourceNeed> ResourceNeeds;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var companyFinancialsMap = CompanyFinancialsManager.GetCompanyFinancialsMap();
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

                    if (ResourceDatas[ResourcePrefabs[outputResource]].m_Weight != 0)
                        continue;

                    var input1Resource = processData.m_Input1.m_Resource;
                    var input2Resource = processData.m_Input2.m_Resource;

                    var currentCashHolding = 0;

                    bool hasNegativeMoney = false;
                    for (int j = 0; j < resources.Length; j++)
                    {
                        if (resources[j].m_Resource == Resource.Money)
                        {
                            if (resources[j].m_Amount < 0)
                            {
                                hasNegativeMoney = true;
                            }
                            currentCashHolding = resources[j].m_Amount;
                            break;
                        }
                    }

                    for (int j = 0; j < resources.Length; j++)
                    {
                        var resource = resources[j];
                        int totalAmount = resource.m_Amount;
                        int exportAmount = totalAmount;

                        if (exportAmount < MINIMAL_EXPORT_AMOUNT) continue;

                        if (resource.m_Resource == outputResource) {
                            // create update CompanyFinancials
                            if (companyFinancialsMap.TryGetValue(entity, out CompanyFinancials financials))
                            {
                                int oldCashHolding = financials.CurrentCashHolding;
                                financials.LastExportEventCashHolding = oldCashHolding;
                                financials.CurrentCashHolding = currentCashHolding;
                                companyFinancialsMap[entity] = financials;

                                //log.Info($"Updated financials for Entity {entity.Index}: Old Cash: {financials.LastExportEventCashHolding}, New Cash: {financials.CurrentCashHolding}");
                            }
                            else
                            {
                                companyFinancialsMap[entity] = new CompanyFinancials
                                {
                                    CurrentCashHolding = currentCashHolding,
                                    LastExportEventCashHolding = currentCashHolding
                                };

                                //log.Info($"Created new financials for Entity {entity.Index}: Initial Cash: {financials.CurrentCashHolding}");
                            }
                        }
                        
                        if (
                            (resource.m_Resource == outputResource) || 
                            (hasNegativeMoney &&
                                ((resource.m_Resource == input1Resource && exportAmount > MINIMAL_STORAGE_AMOUNT) ||
                                (input2Resource != Resource.NoResource && resource.m_Resource == input2Resource && exportAmount > MINIMAL_STORAGE_AMOUNT))
                            )
                        ){
                            if (resource.m_Resource == input1Resource || resource.m_Resource == input2Resource) {
                                exportAmount -= MINIMAL_STORAGE_AMOUNT;
                            }

                            // Check resource needs
                            for (int k = 0; k < ResourceNeeds.Length && exportAmount > 0; k++)
                            {
                                var need = ResourceNeeds[k];
                                if (need.Resource == resource.m_Resource && need.Amount > 0)
                                {
                                    int amountToTransfer = math.min(exportAmount, need.Amount);
                                    if (amountToTransfer > 0)
                                    {
                                        ExportQueue.Enqueue(new VirtualExportEvent
                                        {
                                            m_Seller = entity,
                                            m_Buyer = need.Company,
                                            m_Amount = amountToTransfer,
                                            m_Resource = resource.m_Resource,
                                            m_CurrentCashHolding = currentCashHolding,
                                        });
                                        exportAmount -= amountToTransfer;

                                        // Update or remove the ResourceNeed
                                        if (amountToTransfer == need.Amount)
                                        {
                                            // Mark as invalid by setting Resource to Resource.NoResource
                                            ResourceNeeds[k] = new ResourceNeed
                                            {
                                                Resource = Resource.NoResource,
                                                Company = Entity.Null,
                                                Amount = 0
                                            };
                                        }
                                        else
                                        {
                                            ResourceNeeds[k] = new ResourceNeed
                                            {
                                                Resource = need.Resource,
                                                Company = need.Company,
                                                Amount = need.Amount - amountToTransfer
                                            };
                                        }

                                        if (exportAmount <= 0)
                                        {
                                            break; // All available resources have been exported
                                        }
                                    }
                                }
                            }

                            // Export remaining amount if any
                            if (exportAmount > MINIMAL_EXPORT_AMOUNT)
                            {
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
                // Remove ResourceNeeds with null company Entities, create import events for remaining needs, and create a new list
                for (int i = 0; i < ResourceNeeds.Length; i++)
                {
                    if (ResourceNeeds[i].Company != Entity.Null)
                    {
                        if (ResourceNeeds[i].Amount > 0)
                        {
                            ExportQueue.Enqueue(new VirtualExportEvent
                            {
                                m_Seller = Entity.Null, // Null entity represents import from outside the city
                                m_Buyer = ResourceNeeds[i].Company,
                                m_Amount = ResourceNeeds[i].Amount,
                                m_Resource = ResourceNeeds[i].Resource
                            });
                        }
                    }
                }
            }
        }

        // Remove the BurstCompile attribute to disable Burst compilation
        private struct HandleVirtualExportsJob : IJob
        {
            public BufferLookup<Resources> Resources;
            [ReadOnly] public ComponentLookup<ResourceData> ResourceDatas;
            [ReadOnly] public ResourcePrefabs ResourcePrefabs;
            public NativeQueue<VirtualExportEvent> ExportQueue;

            public void Execute()
            {
                int processedEvents = 0;
                int internalTransfers = 0;
                int externalTransfers = 0;
                int importTransfers = 0;

                while (ExportQueue.TryDequeue(out VirtualExportEvent item))
                {
                    processedEvents++;

                    // Get price x and price y
                    float priceX = EconomyUtils.GetIndustrialPrice(item.m_Resource, ResourcePrefabs, ref ResourceDatas);
                    float priceY = EconomyUtils.GetServicePrice(item.m_Resource, ResourcePrefabs, ref ResourceDatas);
                    float marketPrice = priceX + priceY;

                    if (item.m_Seller == Entity.Null && Resources.HasBuffer(item.m_Buyer))
                    {
                        // Import event
                        importTransfers++;
                        var buyerBuffer = Resources[item.m_Buyer];
                        int importPrice = UnityEngine.Mathf.RoundToInt(marketPrice * item.m_Amount * 0.95f);

                        EconomyUtils.AddResources(item.m_Resource, item.m_Amount, buyerBuffer);
                        EconomyUtils.AddResources(Resource.Money, -importPrice, buyerBuffer);
                    }
                    else if (Resources.HasBuffer(item.m_Seller) && Resources.HasBuffer(item.m_Buyer))
                    {
                        // Internal transfer
                        internalTransfers++;
                        var sellerBuffer = Resources[item.m_Seller];
                        var buyerBuffer = Resources[item.m_Buyer];
                        int buyPrice = UnityEngine.Mathf.RoundToInt(marketPrice * item.m_Amount * 0.9f);
                        int sellPrice = UnityEngine.Mathf.RoundToInt(marketPrice * item.m_Amount * 1.1f);

                        EconomyUtils.AddResources(item.m_Resource, -item.m_Amount, sellerBuffer);
                        EconomyUtils.AddResources(Resource.Money, sellPrice, sellerBuffer);

                        EconomyUtils.AddResources(item.m_Resource, item.m_Amount, buyerBuffer);
                        EconomyUtils.AddResources(Resource.Money, -buyPrice, buyerBuffer);
                    }
                    else if (Resources.HasBuffer(item.m_Seller))
                    {
                        // External transfer (export)
                        externalTransfers++;
                        var resourceBuffer = Resources[item.m_Seller];
                        int exportPrice = UnityEngine.Mathf.RoundToInt(marketPrice * item.m_Amount * 1.05f);

                        EconomyUtils.AddResources(item.m_Resource, -item.m_Amount, resourceBuffer);
                        EconomyUtils.AddResources(Resource.Money, exportPrice, resourceBuffer);
                    }
                }
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
            m_ResourceNeedQueue = new NativeQueue<ResourceNeed>(Allocator.Persistent);

            m_CompanyQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(),
                ComponentType.ReadWrite<WorkProvider>(),
                ComponentType.ReadOnly<UpdateFrame>(),
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
            m_ResourceNeedQueue.Dispose();
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

                var createResourceMapJob = new CreateResourceMapJob
                {
                    EntityType = GetEntityTypeHandle(),
                    ResourcesType = GetBufferTypeHandle<Resources>(true),
                    PrefabRefType = GetComponentTypeHandle<PrefabRef>(true),
                    IndustrialProcessDatas = GetComponentLookup<IndustrialProcessData>(true),
                    ResourcePrefabs = m_ResourceSystem.GetPrefabs(),
                    ResourceDatas = GetComponentLookup<ResourceData>(true),
                    ResourceNeedQueue = m_ResourceNeedQueue.AsParallelWriter()
                };

                log.Info("Scheduling CreateResourceMapJob");
                Dependency = createResourceMapJob.ScheduleParallel(m_CompanyQuery, Dependency);

                Dependency.Complete(); // We need to complete the job to access the queue

                NativeArray<ResourceNeed> resourceNeeds = m_ResourceNeedQueue.ToArray(Allocator.TempJob);

                var virtualExportJob = new VirtualExportJob
                {
                    EntityType = GetEntityTypeHandle(),
                    ResourcesType = GetBufferTypeHandle<Resources>(true),
                    PrefabRefType = GetComponentTypeHandle<PrefabRef>(true),
                    IndustrialProcessDatas = GetComponentLookup<IndustrialProcessData>(true),
                    ResourcePrefabs = m_ResourceSystem.GetPrefabs(),
                    ResourceDatas = GetComponentLookup<ResourceData>(true),
                    ExportQueue = m_ExportQueue.AsParallelWriter(),
                    CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter(),
                    ResourceNeeds = resourceNeeds
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

                // Dispose the temporary array
                Dependency = resourceNeeds.Dispose(Dependency);

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
    }
}