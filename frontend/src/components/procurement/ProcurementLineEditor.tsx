import { useEffect } from 'react';
import { Button, Caption1, Field, Input, Spinner, Tab, TabList, makeStyles, tokens } from '@fluentui/react-components';
import { Delete20Regular } from '@fluentui/react-icons';
import { useQuery } from '@tanstack/react-query';
import { useApi } from '@/api/ApiProvider';
import { DIM_FIELD, DIM_LABEL, getItemDimensions, type StorageDim } from '@/api/itemDimensions';
import type { PrLineForm } from '@/api/purchaseRequisitions';
import { LookupCombobox } from '@/components/LookupCombobox';
import { errorMessage } from '@/components/notify';
import { useSessionStore } from '@/state/sessionStore';

/** A procurement line (requisition or purchase order): item or category, variant, quantity, price, site/warehouse. */
export type ProcurementLine = PrLineForm;

const useStyles = makeStyles({
  line: {
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    padding: '12px',
    display: 'flex',
    flexDirection: 'column',
    rowGap: '8px',
  },
  head: { display: 'flex', alignItems: 'center', columnGap: '8px' },
  spacer: { flexGrow: 1 },
  grid: {
    display: 'grid',
    gap: '8px 12px',
    gridTemplateColumns: '1fr',
    '@media (min-width: 640px)': { gridTemplateColumns: 'repeat(2, 1fr)' },
    '@media (min-width: 1024px)': { gridTemplateColumns: 'repeat(4, 1fr)' },
  },
  wide: { '@media (min-width: 640px)': { gridColumn: 'span 2' } },
  full: { gridColumn: '1 / -1' },
  muted: { color: tokens.colorNeutralForeground3 },
});

export const isProductMaster = (extra?: Record<string, string | null> | null) =>
  !!extra && (extra.ProductSubType === 'ProductMaster' || !!extra.ProductDimensionGroupName);

const noVariant = { variant: '', configuration: '', size: '', color: '', style: '', version: '' };
const noDims = { site: '', warehouse: '', location: '', inventoryStatus: '', requiredDims: undefined, dimsLoading: false };

const sameDims = (a: readonly string[] | undefined, b: readonly string[]) =>
  !!a && a.length === b.length && a.every((x, i) => x === b[i]);

export interface ProcurementLineEditorProps {
  line: ProcurementLine;
  index: number;
  errors: Record<string, string>;
  onChange: (patch: Partial<ProcurementLine>) => void;
  onRemove: () => void;
  canRemove: boolean;
  /** Requisition lines carry their own vendor and currency; PO lines take them from the header. */
  showVendorAndCurrency: boolean;
  /** Storage dimensions this document's lines support (requisitions: Site + Warehouse; orders: + Location, Inventory status). */
  dimensions: readonly StorageDim[];
  /** Header defaults used to pre-fill a mandatory site/warehouse (purchase orders). */
  defaults?: { site?: string; warehouse?: string };
}

export function ProcurementLineEditor({
  line: l, index: i, errors: le, onChange, onRemove, canRemove, showVendorAndCurrency, dimensions, defaults,
}: ProcurementLineEditorProps) {
  const s = useStyles();
  const api = useApi();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);
  const item = l.kind === 'item' ? (l.itemNumber ?? '') : '';

  // The item's storage dimension group decides which dimensions are mandatory; the rest are hidden and kept empty.
  const rules = useQuery({
    queryKey: ['itemDimensions', tenantId, company, item],
    queryFn: ({ signal }) => getItemDimensions(api, item, signal),
    enabled: !!item,
    staleTime: 10 * 60_000,
  });
  const required = rules.data ? dimensions.filter((d) => rules.data.mandatory.includes(d)) : undefined;

  useEffect(() => {
    if (!item) return;
    if (rules.isPending && !l.dimsLoading) { onChange({ dimsLoading: true }); return; }
    if (!required) return;
    if (sameDims(l.requiredDims, required) && !l.dimsLoading) return;
    const patch: Partial<ProcurementLine> = { requiredDims: required, dimsLoading: false };
    for (const d of dimensions) if (!required.includes(d)) patch[DIM_FIELD[d]] = '';
    if (required.includes('Site') && !l.site && defaults?.site) patch.site = defaults.site;
    if (required.includes('Warehouse') && !l.warehouse && defaults?.warehouse && (!defaults.site || (patch.site ?? l.site) === defaults.site))
      patch.warehouse = defaults.warehouse;
    onChange(patch);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [item, rules.isPending, rules.data]);

  // If the rules can't be loaded, don't block the user: D365 still validates on save.
  useEffect(() => {
    if (rules.isError && l.dimsLoading) onChange({ dimsLoading: false, requiredDims: undefined });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rules.isError]);

  /** Item lines show only mandatory dimensions; category lines (no item) keep optional site/warehouse. */
  const shown: { dim: StorageDim; required: boolean }[] =
    l.kind === 'category'
      ? dimensions.filter((d) => d === 'Site' || d === 'Warehouse').map((dim) => ({ dim, required: false }))
      : !item
        ? []
        : rules.isError
          ? dimensions.filter((d) => d === 'Site' || d === 'Warehouse').map((dim) => ({ dim, required: false }))
          : (required ?? []).map((dim) => ({ dim, required: true }));

  const dimField = (d: StorageDim, isRequired: boolean) => {
    const common = { label: DIM_LABEL[d], required: isRequired, validationMessage: le[DIM_FIELD[d]] };
    switch (d) {
      case 'Site':
        return <LookupCombobox key={d} lookup="sites" {...common} value={l.site || null}
          onChange={(id) => onChange({ site: id ?? '', warehouse: '', location: '' })} />;
      case 'Warehouse':
        return <LookupCombobox key={d} lookup="warehouses" {...common} value={l.warehouse || null} filters={{ site: l.site || undefined }}
          onChange={(id) => onChange({ warehouse: id ?? '', location: '' })} />;
      case 'Location':
        return <LookupCombobox key={d} lookup="warehouseLocations" {...common} value={l.location || null}
          filters={{ warehouse: l.warehouse || undefined }} disabled={!l.warehouse} placeholder={l.warehouse ? undefined : 'Select a warehouse first'}
          onChange={(id) => onChange({ location: id ?? '' })} />;
      default:
        return <LookupCombobox key={d} lookup="inventoryStatuses" {...common} value={l.inventoryStatus || null}
          onChange={(id) => onChange({ inventoryStatus: id ?? '' })} />;
    }
  };

  return (
    <div className={s.line} role="group" aria-label={`Line ${i + 1}`}>
      <div className={s.head}>
        <strong>Line {i + 1}</strong>
        <TabList size="small" selectedValue={l.kind}
          onTabSelect={(_, d) => onChange({ ...noVariant, ...noDims, kind: d.value as ProcurementLine['kind'], itemNumber: '', procurementCategory: '', needsVariant: false, onHold: false })}>
          <Tab value="item">Item</Tab>
          <Tab value="category">Category</Tab>
        </TabList>
        <span className={s.spacer} />
        <Button appearance="subtle" icon={<Delete20Regular />} aria-label={`Remove line ${i + 1}`} disabled={!canRemove} onClick={onRemove} />
      </div>
      <div className={s.grid}>
        {l.kind === 'item' ? (
          <LookupCombobox lookup="products" label="Item" required value={l.itemNumber || null} validationMessage={le.itemNumber}
            disabledNote={(it) => (it.extra?.OnHold === 'Yes' ? 'On hold for purchasing' : null)}
            onChange={(id, item) => onChange({
              ...noVariant,
              ...(id === l.itemNumber ? {} : noDims),
              itemNumber: id ?? '',
              description: item?.name ?? l.description,
              unit: item?.extra?.PurchaseUnitSymbol || l.unit,
              needsVariant: isProductMaster(item?.extra),
              onHold: item?.extra?.OnHold === 'Yes',
              itemSalesTaxGroup: item?.extra?.PurchaseSalesTaxItemGroupCode || l.itemSalesTaxGroup,
            })} />
        ) : (
          <LookupCombobox lookup="procurementCategories" label="Procurement category" required value={l.procurementCategory || null}
            validationMessage={le.procurementCategory} onChange={(id) => onChange({ procurementCategory: id ?? '' })} />
        )}
        {l.kind === 'item' && l.needsVariant && l.itemNumber && (
          <LookupCombobox key={`variant-${l.itemNumber}`} lookup="productVariants" label="Variant (product dimensions)" required
            value={l.variant || null} filters={{ item: l.itemNumber }} validationMessage={le.variant}
            onChange={(id, v) => onChange({
              variant: id ?? '',
              configuration: v?.extra?.ProductConfigurationId ?? '',
              size: v?.extra?.ProductSizeId ?? '',
              color: v?.extra?.ProductColorId ?? '',
              style: v?.extra?.ProductStyleId ?? '',
              version: v?.extra?.ProductVersionId ?? '',
            })} />
        )}
        <Field className={s.wide} label="Description" required={l.kind === 'category'} validationMessage={le.description}>
          <Input value={l.description ?? ''} maxLength={60} onChange={(_, d) => onChange({ description: d.value })} />
        </Field>
        <Field label="Quantity" required validationMessage={le.quantity}>
          <Input type="number" min={0} step="any" value={String(l.quantity ?? '')} onChange={(_, d) => onChange({ quantity: d.value })} />
        </Field>
        <LookupCombobox lookup="units" label="Unit" value={l.unit || null} onChange={(id) => onChange({ unit: id ?? '' })} />
        <Field label="Unit price" validationMessage={le.unitPrice}>
          <Input type="number" min={0} step="any" value={String(l.unitPrice ?? '')} onChange={(_, d) => onChange({ unitPrice: d.value })} />
        </Field>
        {showVendorAndCurrency && (
          <>
            <LookupCombobox lookup="currencies" label="Currency" value={l.currency || null} onChange={(id) => onChange({ currency: id ?? '' })} />
            <LookupCombobox lookup="vendors" label="Vendor" value={l.vendor || null}
              onChange={(id, item) => onChange({
                vendor: id ?? '',
                currency: l.currency || item?.extra?.CurrencyCode || '',
                salesTaxGroup: l.salesTaxGroup || item?.extra?.SalesTaxGroupCode || item?.extra?.SalesTaxGroup || '',
              })} />
          </>
        )}
        {shown.map(({ dim, required: r }) => dimField(dim, r))}
        <LookupCombobox lookup="taxGroups" label="Sales tax group" value={l.salesTaxGroup || null}
          placeholder={showVendorAndCurrency ? 'Vendor default' : 'Header default'} onChange={(id) => onChange({ salesTaxGroup: id ?? '' })} />
        <LookupCombobox lookup="taxItemGroups" label="Item sales tax group" value={l.itemSalesTaxGroup || null}
          placeholder="Item default" onChange={(id) => onChange({ itemSalesTaxGroup: id ?? '' })} />
        {l.kind === 'item' && item && (
          <Caption1 className={`${s.full} ${s.muted}`}>
            {rules.isPending ? <Spinner size="extra-tiny" label="Loading storage dimensions…" labelPosition="after" />
              : rules.isError ? `Couldn’t read the item’s storage dimensions (${errorMessage(rules.error)}). D365 checks them on save.`
              : `Storage dimension group ${rules.data.storageDimensionGroup ?? '—'}: ${required?.length ? required.map((d) => DIM_LABEL[d]).join(', ') : 'no'} mandatory${rules.data.source === 'default' ? ' (group setup not readable — site and warehouse assumed)' : ''}. Other storage dimensions are not used on this line.`}
          </Caption1>
        )}
      </div>
    </div>
  );
}
