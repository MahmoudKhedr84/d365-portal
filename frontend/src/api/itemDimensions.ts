import { z } from 'zod';
import type { ApiRequest } from './client';

/** Storage dimensions a procurement line can carry (backend StorageDimension enum). */
export const STORAGE_DIMENSIONS = ['Site', 'Warehouse', 'Location', 'InventoryStatus'] as const;
export type StorageDim = (typeof STORAGE_DIMENSIONS)[number];

/** Line form field holding each dimension's value. */
export const DIM_FIELD: Record<StorageDim, 'site' | 'warehouse' | 'location' | 'inventoryStatus'> = {
  Site: 'site',
  Warehouse: 'warehouse',
  Location: 'location',
  InventoryStatus: 'inventoryStatus',
};

/** Requisition lines carry site and warehouse only; purchase order lines also location and inventory status. */
export const PR_DIMENSIONS: readonly StorageDim[] = ['Site', 'Warehouse'];
export const PO_DIMENSIONS: readonly StorageDim[] = ['Site', 'Warehouse', 'Location', 'InventoryStatus'];

export const DIM_LABEL: Record<StorageDim, string> = {
  Site: 'Site',
  Warehouse: 'Warehouse',
  Location: 'Location',
  InventoryStatus: 'Inventory status',
};

export const itemDimensionRulesSchema = z.object({
  itemNumber: z.string(),
  storageDimensionGroup: z.string().nullable(),
  mandatory: z.array(z.enum(STORAGE_DIMENSIONS)),
  /** group = from the storage dimension group; default = group setup unreadable (Site + Warehouse assumed); none = service item. */
  source: z.string(),
});
export type ItemDimensionRules = z.infer<typeof itemDimensionRulesSchema>;

export async function getItemDimensions(api: ApiRequest, item: string, signal?: AbortSignal): Promise<ItemDimensionRules> {
  return itemDimensionRulesSchema.parse(await api<unknown>(`/items/${encodeURIComponent(item)}/dimensions`, { signal }));
}
