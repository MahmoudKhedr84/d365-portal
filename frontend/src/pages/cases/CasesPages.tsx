import { useState } from 'react';
import {
  Badge,
  Body1,
  Button,
  Card,
  Dropdown,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Option,
  Textarea,
  Title2,
  makeStyles,
  tokens,
  type BadgeProps,
} from '@fluentui/react-components';
import { Send20Regular } from '@fluentui/react-icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useApi } from '@/api/ApiProvider';
import {
  CASE_CLASSIFICATIONS,
  CASE_STATUSES,
  caseFormSchema,
  caseStatusLabel,
  classificationLabel,
  createCase,
  getCase,
  listCases,
  toCaseBody,
  type CaseForm,
  type CaseListItem,
} from '@/api/cases';
import { Crumbs, DetailFrame, DetailHeader, Facts } from '@/components/DetailParts';
import { DocumentListPage } from '@/components/DocumentListPage';
import { LookupCombobox } from '@/components/LookupCombobox';
import { errorMessage, fieldErrors, useNotify } from '@/components/notify';
import { formatDate } from '@/pages/purchaseRequisitions/shared';
import { useSessionStore } from '@/state/sessionStore';

const STATUS_COLORS: Record<string, BadgeProps['color']> = { Planned: 'informative', InProcess: 'warning', Closed: 'success', Cancelled: 'subtle' };
export const CaseStatusBadge = ({ status }: { status: string | null }) => (
  <Badge appearance="tint" color={STATUS_COLORS[status ?? ''] ?? 'informative'}>{caseStatusLabel(status)}</Badge>
);

export function CasesPage() {
  const api = useApi();
  return (
    <DocumentListPage<CaseListItem>
      title="CAPA cases"
      queryKey="cases"
      searchPlaceholder="Search case ID or description"
      newLabel="New case"
      newPath="/cases/new"
      emptyText="No cases found."
      filters={[{ key: 'status', label: 'Status', allLabel: 'All statuses', options: CASE_STATUSES.map((v) => ({ value: v, label: caseStatusLabel(v) })) }]}
      rowKey={(r) => r.caseId}
      fetch={(p, signal) => listCases(api, { search: p.search, top: p.top, skip: p.skip, status: p.filters.status }, signal)}
      columns={[
        { header: 'Case', className: 'mono', render: (r) => <Link to={`/cases/${encodeURIComponent(r.caseId)}`}>{r.caseId}</Link> },
        { header: 'Description', render: (r) => r.description },
        { header: 'Status', render: (r) => <CaseStatusBadge status={r.status} /> },
        { header: 'Classification', render: (r) => classificationLabel(r.classification) },
        { header: 'Category', className: 'mono', render: (r) => r.category },
        { header: 'Due', render: (r) => formatDate(r.dueDate) },
        { header: 'Responsible', className: 'mono', render: (r) => r.responsible },
      ]}
    />
  );
}

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', rowGap: '16px', maxWidth: '1000px' },
  card: { padding: '20px', rowGap: '12px' },
  grid: {
    display: 'grid',
    gap: '12px 16px',
    gridTemplateColumns: '1fr',
    '@media (min-width: 720px)': { gridTemplateColumns: 'repeat(2, 1fr)' },
    '@media (min-width: 1024px)': { gridTemplateColumns: 'repeat(3, 1fr)' },
  },
  full: { '@media (min-width: 720px)': { gridColumn: '1 / -1' } },
  footer: { display: 'flex', columnGap: '12px' },
  muted: { color: tokens.colorNeutralForeground3 },
});

export function CaseCreatePage() {
  const s = useStyles();
  const api = useApi();
  const qc = useQueryClient();
  const notify = useNotify();
  const navigate = useNavigate();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);
  const [form, setForm] = useState<CaseForm>({ description: '', classification: 'Corrective' });
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const set = <K extends keyof CaseForm>(k: K, v: CaseForm[K]) => setForm((f) => ({ ...f, [k]: v }));

  const create = useMutation({
    mutationFn: () => createCase(api, toCaseBody(caseFormSchema.parse(form))),
    onSuccess: (c) => {
      qc.invalidateQueries({ queryKey: ['cases'] });
      qc.setQueryData(['cases', tenantId, company, 'detail', c.caseId], c);
      notify.success(`Case ${c.caseId} created`);
      navigate(`/cases/${encodeURIComponent(c.caseId)}`);
    },
    onError: (e) => setServerError(errorMessage(e)),
  });

  const submit = () => {
    setServerError(null);
    const r = caseFormSchema.safeParse(form);
    const errs = r.success ? {} : fieldErrors(r.error.issues);
    setErrors(errs);
    if (r.success) create.mutate();
  };

  return (
    <div className={s.page}>
      <Crumbs parent="CAPA cases" parentPath="/cases" current="New" />
      <div>
        <Title2 as="h1">New CAPA case</Title2>
        <Body1 className={s.muted}>Created in company <strong>{company}</strong>. D365 assigns the case ID.</Body1>
      </div>
      {serverError && (
        <MessageBar intent="error">
          <MessageBarBody><MessageBarTitle>D365 did not accept the case</MessageBarTitle>{serverError}</MessageBarBody>
        </MessageBar>
      )}
      <Card className={s.card}>
        <div className={s.grid}>
          <Field className={s.full} label="Description" required validationMessage={errors.description}>
            <Input value={form.description} maxLength={60} onChange={(_, d) => set('description', d.value)} />
          </Field>
          <Field label="Classification">
            <Dropdown value={classificationLabel(form.classification)} selectedOptions={[form.classification]}
              onOptionSelect={(_, d) => set('classification', (d.optionValue as CaseForm['classification']) ?? 'Corrective')}>
              {CASE_CLASSIFICATIONS.map((c) => <Option key={c} value={c}>{classificationLabel(c)}</Option>)}
            </Dropdown>
          </Field>
          <LookupCombobox lookup="capaCategories" label="CAPA category" value={form.category || null} onChange={(id) => set('category', id ?? '')} />
          <LookupCombobox lookup="capaTypes" label="CAPA type" value={form.type || null} onChange={(id) => set('type', id ?? '')} />
          <LookupCombobox lookup="capaSources" label="CAPA source" value={form.source || null} onChange={(id) => set('source', id ?? '')} />
          <Field label="Priority" validationMessage={errors.priority}>
            <Input value={form.priority ?? ''} maxLength={20} onChange={(_, d) => set('priority', d.value)} />
          </Field>
          <LookupCombobox lookup="workers" label="Employee responsible" value={form.responsible || null} onChange={(id) => set('responsible', id ?? '')} />
          <Field label="Due date">
            <Input type="date" value={form.dueDate ?? ''} onChange={(_, d) => set('dueDate', d.value)} />
          </Field>
          <Field label="Planned effective date">
            <Input type="date" value={form.plannedEffectiveDate ?? ''} onChange={(_, d) => set('plannedEffectiveDate', d.value)} />
          </Field>
          <Field className={s.full} label="Notes">
            <Textarea value={form.notes ?? ''} resize="vertical" onChange={(_, d) => set('notes', d.value)} />
          </Field>
        </div>
      </Card>
      <div className={s.footer}>
        <Button appearance="primary" icon={<Send20Regular />} onClick={submit} disabled={create.isPending || !company}>
          {create.isPending ? 'Creating in D365…' : 'Create case'}
        </Button>
        <Button onClick={() => navigate('/cases')} disabled={create.isPending}>Cancel</Button>
      </div>
    </div>
  );
}

export function CaseDetailPage() {
  const s = useStyles();
  const api = useApi();
  const { caseId = '' } = useParams();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);
  const c = useQuery({ queryKey: ['cases', tenantId, company, 'detail', caseId], queryFn: () => getCase(api, caseId) });
  const d = c.data;
  return (
    <DetailFrame crumbs={<Crumbs parent="CAPA cases" parentPath="/cases" current={caseId} />}
      loading={c.isLoading} error={c.error} loadingLabel="Loading case from D365…">
      {d && (
        <>
          <DetailHeader title={d.caseId} badges={<CaseStatusBadge status={d.status} />} d365Url={d.d365Url} />
          <Card className={s.card}>
            <Facts items={[
              ['Description', d.description],
              ['Company', d.company],
              ['Classification', classificationLabel(d.classification)],
              ['Category', [d.category, d.subCategory].filter(Boolean).join(' / ')],
              ['Type', d.type],
              ['Source', d.source],
              ['Priority', d.priority],
              ['Due date', formatDate(d.dueDate)],
              ['Planned effective date', formatDate(d.plannedEffectiveDate)],
              ['Employee responsible', [d.responsible, d.responsibleName].filter(Boolean).join(' · ')],
              ['Created', d.createdAt ? new Date(d.createdAt).toLocaleString() : null],
            ]} />
            {d.notes && <Facts items={[['Notes', d.notes]]} />}
          </Card>
        </>
      )}
    </DetailFrame>
  );
}
