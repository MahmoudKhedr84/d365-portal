import { Button, Field, Input, Tab, TabList, makeStyles, tokens } from '@fluentui/react-components';
import { Delete20Regular } from '@fluentui/react-icons';
import type { PrLineForm } from '@/api/purchaseRequisitions';
import { LookupCombobox } from '@/components/LookupCombobox';

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
});

export const isProductMaster = (extra?: Record<string, string | null> | null) =>
  !!extra && (extra.ProductSubType === 'ProductMaster' || !!extra.ProductDimensionGroupName);

const noVariant = { variant: '', configuration: '', size: '', color: '', style: '', version: '' };

export interface ProcurementLineEditorProps {
  line: ProcurementLine;
  index: number;
  errors: Record<string, string>;
  onChange: (patch: Partial<ProcurementLine>) => void;
  onRemove: () => void;
  canRemove: boolean;
  /** Requisition lines carry their own vendor and currency; PO lines take them from the header. */
  showVendorAndCurrency: boolean;
}

export function ProcurementLineEditor({ line: l, index: i, errors: le, onChange, onRemove, canRemove, showVendorAndCurrency }: ProcurementLineEditorProps) {
  const s = useStyles();
  return (
    <div className={s.line} role="group" aria-label={`Line ${i + 1}`}>
      <div className={s.head}>
        <strong>Line {i + 1}</strong>
        <TabList size="small" selectedValue={l.kind}
          onTabSelect={(_, d) => onChange({ ...noVariant, kind: d.value as ProcurementLine['kind'], itemNumber: '', procurementCategory: '', needsVariant: false, onHold: false })}>
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
              itemNumber: id ?? '',
              description: item?.name ?? l.description,
              unit: item?.extra?.PurchaseUnitSymbol || l.unit,
              needsVariant: isProductMaster(item?.extra),
              onHold: item?.extra?.OnHold === 'Yes',
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
              onChange={(id, item) => onChange({ vendor: id ?? '', currency: l.currency || item?.extra?.CurrencyCode || '' })} />
          </>
        )}
        <LookupCombobox lookup="sites" label="Site" value={l.site || null} onChange={(id) => onChange({ site: id ?? '', warehouse: '' })} />
        <LookupCombobox lookup="warehouses" label="Warehouse" value={l.warehouse || null} filters={{ site: l.site || undefined }}
          onChange={(id) => onChange({ warehouse: id ?? '' })} />
      </div>
    </div>
  );
}
