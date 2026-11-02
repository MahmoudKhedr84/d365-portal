import { useState } from 'react';
import {
  Body1,
  Breadcrumb,
  BreadcrumbButton,
  BreadcrumbDivider,
  BreadcrumbItem,
  Button,
  Caption1,
  Card,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Subtitle2,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Add20Regular, Send20Regular } from '@fluentui/react-icons';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { useApi } from '@/api/ApiProvider';
import { ApiError } from '@/api/client';
import { createPurchaseOrder, poFormSchema, toPoCreateBody, type PoForm } from '@/api/purchaseOrders';
import { emptyLine, estimateTotals, prLineFormSchema, type PrLineForm } from '@/api/purchaseRequisitions';
import { LookupCombobox } from '@/components/LookupCombobox';
import { errorMessage, fieldErrors, useNotify } from '@/components/notify';
import { ProcurementLineEditor } from '@/components/procurement/ProcurementLineEditor';
import { formatAmount } from '@/pages/purchaseRequisitions/shared';
import { useSessionStore } from '@/state/sessionStore';

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', rowGap: '16px', maxWidth: '1100px' },
  card: { padding: '20px', rowGap: '12px' },
  grid: {
    display: 'grid',
    gap: '12px 16px',
    gridTemplateColumns: '1fr',
    '@media (min-width: 720px)': { gridTemplateColumns: 'repeat(2, 1fr)' },
    '@media (min-width: 1024px)': { gridTemplateColumns: 'repeat(3, 1fr)' },
  },
  footer: { display: 'flex', alignItems: 'center', columnGap: '12px', flexWrap: 'wrap', rowGap: '8px' },
  spacer: { flexGrow: 1 },
  totals: { fontFamily: tokens.fontFamilyMonospace, fontWeight: tokens.fontWeightSemibold },
  muted: { color: tokens.colorNeutralForeground3 },
});

export function PurchaseOrderCreatePage() {
  const s = useStyles();
  const api = useApi();
  const qc = useQueryClient();
  const notify = useNotify();
  const navigate = useNavigate();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);

  const [form, setForm] = useState<PoForm>({ vendor: '', currency: '', requestedDeliveryDate: '', accountingDate: '', site: '', warehouse: '', vendorReference: '', requester: '', salesTaxGroup: '', purchaseOrderNumber: '' });
  const [lines, setLines] = useState<PrLineForm[]>([emptyLine()]);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [lineErrors, setLineErrors] = useState<Record<string, string>[]>([]);
  const [serverError, setServerError] = useState<string | null>(null);

  const set = <K extends keyof PoForm>(k: K, v: PoForm[K]) => setForm((f) => ({ ...f, [k]: v }));
  const setLine = (i: number, patch: Partial<PrLineForm>) => setLines((ls) => ls.map((l, idx) => (idx === i ? { ...l, ...patch } : l)));

  const create = useMutation({
    mutationFn: () => createPurchaseOrder(api, toPoCreateBody(poFormSchema.parse(form), lines)),
    onSuccess: (po) => {
      qc.invalidateQueries({ queryKey: ['po'] });
      qc.setQueryData(['po', tenantId, company, 'detail', po.purchaseOrderNumber], po);
      notify.success(`Purchase order ${po.purchaseOrderNumber} created`, `Company ${po.company}. Confirm or submit it for approval in D365.`);
      navigate(`/purchase-orders/${encodeURIComponent(po.purchaseOrderNumber)}`);
    },
    onError: (e) => {
      if (e instanceof ApiError && e.code === 'po_number_required') {
        setErrors((x) => ({ ...x, purchaseOrderNumber: 'Enter the purchase order number (numbering is manual in this company).' }));
      }
      setServerError(errorMessage(e));
    },
  });

  const submit = () => {
    setServerError(null);
    const head = poFormSchema.safeParse(form);
    const headErrors = head.success ? {} : fieldErrors(head.error.issues);
    const perLine = lines.map((l) => {
      const r = prLineFormSchema.safeParse(l);
      return r.success ? {} : fieldErrors(r.error.issues);
    });
    setErrors(headErrors);
    setLineErrors(perLine);
    if (Object.keys(headErrors).length === 0 && perLine.every((e) => Object.keys(e).length === 0)) create.mutate();
  };

  const total = estimateTotals(lines.map((l) => ({ ...l, currency: form.currency })))[0];

  return (
    <div className={s.page}>
      <Breadcrumb aria-label="Breadcrumb">
        <BreadcrumbItem><BreadcrumbButton onClick={() => navigate('/purchase-orders')}>Purchase orders</BreadcrumbButton></BreadcrumbItem>
        <BreadcrumbDivider />
        <BreadcrumbItem><BreadcrumbButton current>New</BreadcrumbButton></BreadcrumbItem>
      </Breadcrumb>
      <div>
        <Title2 as="h1">New purchase order</Title2>
        <Body1 className={s.muted}>Created in company <strong>{company}</strong>. D365 assigns the number and fills vendor defaults.</Body1>
      </div>

      {serverError && (
        <MessageBar intent="error">
          <MessageBarBody><MessageBarTitle>D365 did not accept the purchase order</MessageBarTitle>{serverError}</MessageBarBody>
        </MessageBar>
      )}

      <Card className={s.card}>
        <Subtitle2 as="h2">Header</Subtitle2>
        <div className={s.grid}>
          <LookupCombobox lookup="vendors" label="Vendor" required value={form.vendor || null} validationMessage={errors.vendor}
            onChange={(id, v) => setForm((f) => ({
              ...f,
              vendor: id ?? '',
              currency: f.currency || v?.extra?.CurrencyCode || '',
              salesTaxGroup: f.salesTaxGroup || v?.extra?.SalesTaxGroupCode || v?.extra?.SalesTaxGroup || '',
            }))} />
          <LookupCombobox lookup="currencies" label="Currency" value={form.currency || null} placeholder="Vendor default"
            onChange={(id) => set('currency', id ?? '')} />
          <Field label="Vendor reference" validationMessage={errors.vendorReference}>
            <Input value={form.vendorReference} maxLength={60} onChange={(_, d) => set('vendorReference', d.value)} />
          </Field>
          <Field label="Requested receipt date">
            <Input type="date" value={form.requestedDeliveryDate} onChange={(_, d) => set('requestedDeliveryDate', d.value)} />
          </Field>
          <Field label="Accounting date">
            <Input type="date" value={form.accountingDate} onChange={(_, d) => set('accountingDate', d.value)} />
          </Field>
          <LookupCombobox lookup="workers" label="Requester (optional)" value={form.requester || null}
            onChange={(id) => set('requester', id ?? '')} />
          <LookupCombobox lookup="taxGroups" label="Sales tax group" value={form.salesTaxGroup || null} placeholder="Vendor default"
            onChange={(id) => set('salesTaxGroup', id ?? '')} />
          <Field label="Purchase order number" hint="Leave empty unless numbering is manual" validationMessage={errors.purchaseOrderNumber}>
            <Input value={form.purchaseOrderNumber} maxLength={20} onChange={(_, d) => set('purchaseOrderNumber', d.value)} />
          </Field>
          <LookupCombobox lookup="sites" label="Default site" value={form.site || null}
            onChange={(id) => setForm((f) => ({ ...f, site: id ?? '', warehouse: '' }))} />
          <LookupCombobox lookup="warehouses" label="Default warehouse" value={form.warehouse || null}
            filters={{ site: form.site || undefined }} onChange={(id) => set('warehouse', id ?? '')} />
        </div>
      </Card>

      <Card className={s.card}>
        <Subtitle2 as="h2">Lines</Subtitle2>
        {lines.map((l, i) => (
          <ProcurementLineEditor key={i} line={l} index={i} errors={lineErrors[i] ?? {}} showVendorAndCurrency={false}
            onChange={(patch) => setLine(i, patch)} canRemove={lines.length > 1}
            onRemove={() => { setLines((ls) => ls.filter((_, idx) => idx !== i)); setLineErrors((le) => le.filter((_, idx) => idx !== i)); }} />
        ))}
        <div className={s.footer}>
          <Button icon={<Add20Regular />} onClick={() => setLines((ls) => [...ls, emptyLine()])} disabled={lines.length >= 200}>Add line</Button>
          <span className={s.spacer} />
          <Caption1 className={s.muted}>Estimated total</Caption1>
          <span className={s.totals} aria-label="Estimated total">{formatAmount(total?.amount ?? 0, form.currency || null)}</span>
        </div>
      </Card>

      <div className={s.footer}>
        <Button appearance="primary" icon={<Send20Regular />} onClick={submit} disabled={create.isPending || !company}>
          {create.isPending ? 'Creating in D365…' : 'Create purchase order'}
        </Button>
        <Button onClick={() => navigate('/purchase-orders')} disabled={create.isPending}>Cancel</Button>
      </div>
    </div>
  );
}
