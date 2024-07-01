using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;

namespace ProfitBasedIndustryAndOffice.Prefabs
{
    public struct CompanyFinancials
    {
        public int CurrentCashHolding;
        public int LastExportEventCashHolding;
        public int HeadCount;
    }

    internal class CompanyFinancialsManager
    {
        private static NativeHashMap<Entity, CompanyFinancials> s_CompanyFinancialsMap;

        public static void Initialize(int capacity)
        {
            s_CompanyFinancialsMap = new NativeHashMap<Entity, CompanyFinancials>(capacity, Allocator.Persistent);
        }

        public static void Dispose()
        {
            if (s_CompanyFinancialsMap.IsCreated)
            {
                s_CompanyFinancialsMap.Dispose();
            }
        }

        public static NativeHashMap<Entity, CompanyFinancials> GetCompanyFinancialsMap()
        {
            return s_CompanyFinancialsMap;
        }

        public static bool TryGetCompanyFinancials(Entity company, out CompanyFinancials financials)
        {
            return s_CompanyFinancialsMap.TryGetValue(company, out financials);
        }
    }
}
