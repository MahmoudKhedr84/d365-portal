import { z } from 'zod';
import type { ApiRequest } from './client';

export const dashboardSchema = z.object({
  company: z.string(),
  kpis: z.array(z.object({ key: z.string(), label: z.string(), value: z.number().nullable(), link: z.string(), error: z.string().nullable() })),
  createdPerDay: z.array(z.object({ date: z.string(), count: z.number() })),
  createdByType: z.record(z.number()),
  failedD365CallsLast7Days: z.number(),
  recentActivity: z.array(z.object({
    timestamp: z.string(), userEmail: z.string().nullable(), company: z.string().nullable(), entity: z.string(), summary: z.string().nullable(),
  })),
  recentPurchaseOrders: z.array(z.object({
    purchaseOrderNumber: z.string(), vendorName: z.string().nullable(), status: z.string().nullable(),
    approvalStatus: z.string().nullable(), requestedDeliveryDate: z.string().nullable(),
  })),
  error: z.string().nullable(),
});
export type Dashboard = z.infer<typeof dashboardSchema>;

export const documentLabel: Record<string, string> = {
  PurchaseRequisition: 'Purchase requisitions',
  PurchaseOrder: 'Purchase orders',
  CAPACase: 'CAPA cases',
  GeneralJournal: 'General journals',
};
export const documentLink: Record<string, string> = {
  PurchaseRequisition: '/purchase-requisitions',
  PurchaseOrder: '/purchase-orders',
  CAPACase: '/cases',
  GeneralJournal: '/general-journals',
};

export async function getDashboard(api: ApiRequest, signal?: AbortSignal): Promise<Dashboard> {
  return dashboardSchema.parse(await api<unknown>('/dashboard', { signal }));
}
