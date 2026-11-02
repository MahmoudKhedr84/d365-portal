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
  Dropdown,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Option,
  Subtitle2,
  Textarea,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Add20Regular, Send20Regular } from '@fluentui/react-icons';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { useApi } from '@/api/ApiProvider';
import { ApiError } from '@/api/client';
import {
  createPurchaseRequisition,
  emptyLine,
  estimateTotals,
  prFormSchema,
  prLineFormSchema,
  toCreateBody,
  type PrForm,
  type PrLineForm,
} from '@/api/purchaseRequisitions';
import { LookupCombobox } from '@/components/LookupCombobox';
import { ProcurementLineEditor } from '@/components/procurement/ProcurementLineEditor';
import { errorMessage, fieldErrors, useNotify } from '@/components/notify';
import { useSessionStore } from '@/state/sessionStore';
import { formatAmount } from './shared';

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
  full: { '@media (min-width: 720px)': { gridColumn: '1 / -1' } },
  line: {
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    padding: '12px',
    display: 'flex',
    flexDirection: 'column',
    rowGap: '8px',
  },
  lineHead: { display: 'flex', alignItems: 'center', columnGap: '8px' },
  spacer: { flexGrow: 1 },
  lineGrid: {
    display: 'grid',
    gap: '8px 12px',
    gridTemplateColumns: '1fr',
    '@media (min-width: 640px)': { gridTemplateColumns: 'repeat(2, 1fr)' },
    '@media (min-width: 1024px)': { gridTemplateColumns: 'repeat(4, 1fr)' },
  },
  wide: { '@media (min-width: 640px)': { gridColumn: 'span 2' } },
  footer: { display: 'flex', alignItems: 'center', columnGap: '12px', flexWrap: 'wrap', rowGap: '8px' },
  totals: { fontFamily: tokens.fontFamilyMonospace, fontWeight: tokens.fontWeightSemibold },
  muted: { color: tokens.colorNeutralForeground3 },
});

const requesterKey = (tenantId: string | null) => `d365portal.requester.${tenantId ?? ''}`;
function readRequester(tenantId: string | null) {
  try { return localStorage.getItem(requesterKey(tenantId)) ?? ''; } catch { return ''; }
}
function saveRequester(tenantId: string | null, v: string) {
  try { localStorage.setItem(requesterKey(tenantId), v); } catch { /* storage unavailable */ }
}


type LineErrors = Record<string, string>;

export function PurchaseRequisitionCreatePage() {
  const s = useStyles();
  const api = useApi();
  const qc = useQueryClient();
  const notify = useNotify();
  const navigate = useNavigate();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);

  const [form, setForm] = useState<PrForm>(() => ({
    name: '', purpose: 'Consumption', requester: readRequester(tenantId), requestedDate: '', accountingDate: '', justification: '',
  }));
  const [lines, setLines] = useState<PrLineForm[]>([emptyLine()]);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [lineErrors, setLineErrors] = useState<LineErrors[]>([]);
  const [serverError, setServerError] = useState<string | null>(null);

  const set = <K extends keyof PrForm>(k: K, v: PrForm[K]) => setForm((f) => ({ ...f, [k]: v }));
  const setLine = (i: number, patch: Partial<PrLineForm>) =>
    setLines((ls) => ls.map((l, idx) => (idx === i ? { ...l, ...patch } : l)));

  const create = useMutation({
    mutationFn: () => createPurchaseRequisition(api, toCreateBody(prFormSchema.parse(form), lines)),
    onSuccess: (pr) => {
      saveRequester(tenantId, form.requester ?? '');
      qc.invalidateQueries({ queryKey: ['pr'] });
      qc.setQueryData(['pr', tenantId, company, 'detail', pr.requisitionNumber], pr);
      notify.success(`Requisition ${pr.requisitionNumber} created`, 'It is a draft in D365. Submit it for approval there.');
      navigate(`/purchase-requisitions/${encodeURIComponent(pr.requisitionNumber)}`);
    },
    onError: (e) => {
      setServerError(errorMessage(e));
      if (e instanceof ApiError && e.code === 'validation_failed') window.scrollTo?.({ top: 0 });
    },
  });

  const submit = () => {
    setServerError(null);
    const head = prFormSchema.safeParse(form);
    const headErrors = head.success ? {} : fieldErrors(head.error.issues);
    const perLine = lines.map((l) => {
      const r = prLineFormSchema.safeParse(l);
      return r.success ? {} : fieldErrors(r.error.issues);
    });
    if (lines.length === 0) headErrors.lines = 'Add at least one line.';
    setErrors(headErrors);
    setLineErrors(perLine);
    if (Object.keys(headErrors).length === 0 && perLine.every((e) => Object.keys(e).length === 0)) create.mutate();
  };

  const totals = estimateTotals(lines);

  return (
    <div className={s.page}>
      <Breadcrumb aria-label="Breadcrumb">
        <BreadcrumbItem><BreadcrumbButton onClick={() => navigate('/purchase-requisitions')}>Purchase requisitions</BreadcrumbButton></BreadcrumbItem>
        <BreadcrumbDivider />
        <BreadcrumbItem><BreadcrumbButton current>New</BreadcrumbButton></BreadcrumbItem>
      </Breadcrumb>
      <div>
        <Title2 as="h1">New purchase requisition</Title2>
        <Body1 className={s.muted}>Lines are created for company <strong>{company}</strong>. D365 assigns the requisition number.</Body1>
      </div>

      {serverError && (
        <MessageBar intent="error">
          <MessageBarBody><MessageBarTitle>D365 did not accept the requisition</MessageBarTitle>{serverError}</MessageBarBody>
        </MessageBar>
      )}

      <Card className={s.card}>
        <Subtitle2 as="h2">Header</Subtitle2>
        <div className={s.grid}>
          <Field label="Name" required validationMessage={errors.name}>
            <Input value={form.name} maxLength={60} onChange={(_, d) => set('name', d.value)} />
          </Field>
          <Field label="Purpose">
            <Dropdown value={form.purpose} selectedOptions={[form.purpose]}
              onOptionSelect={(_, d) => set('purpose', (d.optionValue as PrForm['purpose']) ?? 'Consumption')}>
              <Option value="Consumption">Consumption</Option>
              <Option value="Replenishment">Replenishment</Option>
            </Dropdown>
          </Field>
          <LookupCombobox lookup="workers" label="Requester (optional)" value={form.requester || null}
            placeholder="Defaults to the preparer" onChange={(id) => set('requester', id ?? '')} />
          <Field label="Requested date">
            <Input type="date" value={form.requestedDate} onChange={(_, d) => set('requestedDate', d.value)} />
          </Field>
          <Field label="Accounting date">
            <Input type="date" value={form.accountingDate} onChange={(_, d) => set('accountingDate', d.value)} />
          </Field>
          <Field className={s.full} label="Business justification">
            <Textarea value={form.justification} resize="vertical" onChange={(_, d) => set('justification', d.value)} />
          </Field>
        </div>
      </Card>

      <Card className={s.card}>
        <Subtitle2 as="h2">Lines</Subtitle2>
        {errors.lines && <Caption1 style={{ color: tokens.colorPaletteRedForeground1 }}>{errors.lines}</Caption1>}
        {lines.map((l, i) => (
          <ProcurementLineEditor key={i} line={l} index={i} errors={lineErrors[i] ?? {}} showVendorAndCurrency
            onChange={(patch) => setLine(i, patch)} canRemove={lines.length > 1}
            onRemove={() => { setLines((ls) => ls.filter((_, idx) => idx !== i)); setLineErrors((le) => le.filter((_, idx) => idx !== i)); }} />
        ))}
        <div className={s.footer}>
          <Button icon={<Add20Regular />} onClick={() => setLines((ls) => [...ls, emptyLine()])} disabled={lines.length >= 100}>Add line</Button>
          <span className={s.spacer} />
          <Caption1 className={s.muted}>Estimated total</Caption1>
          <span className={s.totals} aria-label="Estimated total">
            {totals.map((t) => formatAmount(t.amount, t.currency || null)).join(' · ')}
          </span>
        </div>
      </Card>

      <div className={s.footer}>
        <Button appearance="primary" icon={<Send20Regular />} onClick={submit} disabled={create.isPending || !company}>
          {create.isPending ? 'Creating in D365…' : 'Create requisition'}
        </Button>
        <Button onClick={() => navigate('/purchase-requisitions')} disabled={create.isPending}>Cancel</Button>
      </div>
    </div>
  );
}
