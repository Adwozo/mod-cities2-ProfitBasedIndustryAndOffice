using Unity.Collections;
using Unity.Entities;

namespace ProfitBasedIndustryAndOffice.Prefabs
{
    public struct CompanyFinancials
    {
        public int LastCashBalance;
        public int HeadCount;
        public bool Initialized;
    }

    internal static class CompanyFinancialsManager
    {
        private static NativeParallelHashMap<Entity, CompanyFinancials> s_CompanyFinancialsMap;

        public static void Initialize(int capacity)
        {
            if (s_CompanyFinancialsMap.IsCreated)
            {
                s_CompanyFinancialsMap.Dispose();
            }

            s_CompanyFinancialsMap = new NativeParallelHashMap<Entity, CompanyFinancials>(capacity, Allocator.Persistent);
        }

        public static void Dispose()
        {
            if (s_CompanyFinancialsMap.IsCreated)
            {
                s_CompanyFinancialsMap.Dispose();
            }
        }

        public static NativeParallelHashMap<Entity, CompanyFinancials> GetCompanyFinancialsMap()
        {
            return s_CompanyFinancialsMap;
        }

        public static bool TryGetCompanyFinancials(Entity company, out CompanyFinancials financials)
        {
            if (!s_CompanyFinancialsMap.IsCreated)
            {
                financials = default;
                return false;
            }

            return s_CompanyFinancialsMap.TryGetValue(company, out financials);
        }
    }
}
