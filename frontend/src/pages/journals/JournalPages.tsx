import { useState } from 'react';
import {
  Badge,
  Body1,
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
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Add20Regular, Copy20Regular, Delete20Regular, Send20Regular } from '@fluentui/react-icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useApi } from '@/api/ApiProvider';
import {
  ACCOUNT_TYPES,
  accountLookup,
  accountTypeLabel,
  createJournal,
  emptyJournalLine,
  getJournal,
  journalTotals,
  listJournals,
  toJournalBody,
  validateJournalLine,
  type AccountType,
  type JournalLineForm,
  type JournalListItem,
} from '@/api/journals';
import { Crumbs, DetailFrame, DetailHeader, Facts } from '@/components/DetailParts';
import { DocumentListPage } from '@/components/DocumentListPage';
import { LookupCombobox } from '@/components/LookupCombobox';
import { errorMessage, useNotify } from '@/components/notify';
import { formatAmount, formatDate } from '@/pages/purchaseRequisitions/shared';
import { useSessionStore } from '@/state/sessionStore';

const PostedBadge = ({ posted }: { posted: boolean }) =>
  posted ? <Badge appearance="tint" color="success">Posted</Badge> : <Badge appearance="tint" color="informative">Open</Badge>;

export function JournalsPage() {
  const api = useApi();
  return (
    <DocumentListPage<JournalListItem>
      title="General journals"
      queryKey="journals"
      searchPlaceholder="Search number, name or description"
      newLabel="New journal"
      newPath="/general-journals/new"
      emptyText="No journals found."
      filters={[{ key: 'posted', label: 'Posted', allLabel: 'Posted and open', options: [{ value: 'false', label: 'Open' }, { value: 'true', label: 'Posted' }] }]}
      rowKey={(r) => r.journalBatchNumber}
      fetch={(p, signal) => listJournals(api, { search: p.search, top: p.top, skip: p.skip, posted: p.filters.posted }, signal)}
      columns={[
        { header: 'Journal', className: 'mono', render: (r) => <Link to={`/general-journals/${encodeURIComponent(r.journalBatchNumber)}`}>{r.journalBatchNumber}</Link> },
        { header: 'Name', className: 'mono', render: (r) => r.journalName },
        { header: 'Description', render: (r) => r.description },
        { header: 'Status', render: (r) => <PostedBadge posted={r.posted} /> },
        { header: 'Debit', className: 'num', render: (r) => formatAmount(r.totalDebit) },
        { header: 'Credit', className: 'num', render: (r) => formatAmount(r.totalCredit) },
      ]}
    />
  );
}

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', rowGap: '16px' },
  card: { padding: '20px', rowGap: '12px' },
  headGrid: {
    display: 'grid',
    gap: '12px 16px',
    gridTemplateColumns: '1fr',
    '@media (min-width: 720px)': { gridTemplateColumns: '1fr 2fr' },
  },
  line: {
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    padding: '10px 12px',
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
    '@media (min-width: 1100px)': { gridTemplateColumns: 'repeat(6, 1fr)' },
  },
  span2: { '@media (min-width: 640px)': { gridColumn: 'span 2' } },
  totals: {
    position: 'sticky',
    bottom: 0,
    zIndex: 1,
    padding: '12px 16px',
    display: 'flex',
    flexWrap: 'wrap',
    gap: '8px 24px',
    alignItems: 'center',
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow8,
    borderRadius: tokens.borderRadiusMedium,
  },
  total: { fontFamily: tokens.fontFamilyMonospace, fontVariantNumeric: 'tabular-nums' },
  ok: { color: tokens.colorPaletteGreenForeground1, fontWeight: tokens.fontWeightSemibold },
  off: { color: tokens.colorPaletteRedForeground1, fontWeight: tokens.fontWeightSemibold },
  muted: { color: tokens.colorNeutralForeground3 },
  scroll: { overflowX: 'auto' },
  table: { minWidth: '980px' },
  mono: { fontFamily: tokens.fontFamilyMonospace },
  num: { textAlign: 'end', fontVariantNumeric: 'tabular-nums' },
});

function AccountTypeDropdown({ label, value, onChange, allowNone }: {
  label: string; value: AccountType | ''; onChange: (v: AccountType | '') => void; allowNone?: boolean;
}) {
  return (
    <Field label={label}>
      <Dropdown value={value ? accountTypeLabel(value) : 'None'} selectedOptions={[value]}
        onOptionSelect={(_, d) => onChange((d.optionValue ?? '') as AccountType | '')}>
        {allowNone && <Option value="">None</Option>}
        {ACCOUNT_TYPES.map((t) => <Option key={t} value={t}>{accountTypeLabel(t)}</Option>)}
      </Dropdown>
    </Field>
  );
}

/** Running totals per currency: debit, credit and balance (green when balanced). */
export function JournalTotalsBar({ lines }: { lines: JournalLineForm[] }) {
  const s = useStyles();
  const totals = journalTotals(lines);
  return (
    <div className={s.totals} aria-label="Running totals">
      <Caption1 className={s.muted}>Running totals</Caption1>
      {totals.map((t) => (
        <span key={t.currency} className={s.total}>
          {t.currency} · Debit {formatAmount(t.debit)} · Credit {formatAmount(t.credit)} ·{' '}
          <span className={t.balance === 0 ? s.ok : s.off}>
            {t.balance === 0 ? 'Balanced' : `Out of balance ${formatAmount(t.balance)}`}
          </span>
        </span>
      ))}
    </div>
  );
}

export function JournalCreatePage() {
  const s = useStyles();
  const api = useApi();
  const qc = useQueryClient();
  const notify = useNotify();
  const navigate = useNavigate();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);

  const [journalName, setJournalName] = useState('');
  const [defaultCurrency, setDefaultCurrency] = useState('');
  const [description, setDescription] = useState('');
  const [lines, setLines] = useState<JournalLineForm[]>([emptyJournalLine(), emptyJournalLine()]);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [lineErrors, setLineErrors] = useState<Record<string, string>[]>([]);
  const [serverError, setServerError] = useState<string | null>(null);

  const setLine = (i: number, patch: Partial<JournalLineForm>) => setLines((ls) => ls.map((l, idx) => (idx === i ? { ...l, ...patch } : l)));

  const create = useMutation({
    mutationFn: () => createJournal(api, toJournalBody(journalName, description, lines)),
    onSuccess: (j) => {
      qc.invalidateQueries({ queryKey: ['journals'] });
      qc.setQueryData(['journals', tenantId, company, 'detail', j.journalBatchNumber], j);
      notify.success(`Journal ${j.journalBatchNumber} created`, 'Validate and post it in D365.');
      navigate(`/general-journals/${encodeURIComponent(j.journalBatchNumber)}`);
    },
    onError: (e) => setServerError(errorMessage(e)),
  });

  const submit = () => {
    setServerError(null);
    const head: Record<string, string> = {};
    if (!journalName.trim()) head.journalName = 'Select a journal name.';
    const perLine = lines.map(validateJournalLine);
    setErrors(head);
    setLineErrors(perLine);
    if (Object.keys(head).length === 0 && perLine.every((e) => Object.keys(e).length === 0)) create.mutate();
  };

  const unbalanced = journalTotals(lines).some((t) => t.balance !== 0);

  return (
    <div className={s.page}>
      <Crumbs parent="General journals" parentPath="/general-journals" current="New" />
      <div>
        <Title2 as="h1">New general journal</Title2>
        <Body1 className={s.muted}>Created in company <strong>{company}</strong>. D365 assigns the journal number and vouchers; post it in D365.</Body1>
      </div>
      {serverError && (
        <MessageBar intent="error">
          <MessageBarBody><MessageBarTitle>D365 did not accept the journal</MessageBarTitle>{serverError}</MessageBarBody>
        </MessageBar>
      )}

      <Card className={s.card}>
        <div className={s.headGrid}>
          <LookupCombobox lookup="journalNames" label="Journal name" required value={journalName || null} validationMessage={errors.journalName}
            onChange={(id, item) => {
              setJournalName(id ?? '');
              const cur = item?.extra?.Currency ?? '';
              if (cur) {
                setDefaultCurrency(cur);
                setLines((ls) => ls.map((l) => (l.currency ? l : { ...l, currency: cur })));
              }
            }} />
          <Field label="Description">
            <Input value={description} maxLength={60} onChange={(_, d) => setDescription(d.value)} />
          </Field>
        </div>
      </Card>

      <Card className={s.card}>
        <Subtitle2 as="h2">Lines</Subtitle2>
        {lines.map((l, i) => {
          const le = lineErrors[i] ?? {};
          return (
            <div key={i} className={s.line} role="group" aria-label={`Line ${i + 1}`}>
              <div className={s.lineHead}>
                <strong>Line {i + 1}</strong>
                <span className={s.spacer} />
                <Button appearance="subtle" icon={<Copy20Regular />} aria-label={`Copy line ${i + 1}`}
                  onClick={() => setLines((ls) => [...ls.slice(0, i + 1), { ...l }, ...ls.slice(i + 1)])} />
                <Button appearance="subtle" icon={<Delete20Regular />} aria-label={`Remove line ${i + 1}`} disabled={lines.length === 1}
                  onClick={() => { setLines((ls) => ls.filter((_, idx) => idx !== i)); setLineErrors((x) => x.filter((_, idx) => idx !== i)); }} />
              </div>
              <div className={s.lineGrid}>
                <Field label="Date" required validationMessage={le.date}>
                  <Input type="date" value={l.date} onChange={(_, d) => setLine(i, { date: d.value })} />
                </Field>
                <AccountTypeDropdown label="Account type" value={l.accountType}
                  onChange={(v) => setLine(i, { accountType: (v || 'Ledger') as AccountType, account: '', dimensions: '' })} />
                <LookupCombobox key={`acc-${l.accountType}`} lookup={accountLookup[l.accountType]} label="Account" required
                  value={l.account || null} validationMessage={le.account}
                  onChange={(id, item) => setLine(i, {
                    account: id ?? '',
                    currency: l.currency || item?.extra?.CurrencyCode || item?.extra?.SalesCurrencyCode || defaultCurrency,
                  })} />
                {l.accountType === 'Ledger' ? (
                  <Field label="Dimensions" hint="Segments after the main account, e.g. 001-022">
                    <Input value={l.dimensions} onChange={(_, d) => setLine(i, { dimensions: d.value })} />
                  </Field>
                ) : <span />}
                <Field className={s.span2} label="Description">
                  <Input value={l.text} maxLength={512} onChange={(_, d) => setLine(i, { text: d.value })} />
                </Field>
                <Field label="Debit" validationMessage={le.amount}>
                  <Input type="number" min={0} step="any" value={l.debit} onChange={(_, d) => setLine(i, { debit: d.value, credit: d.value ? '' : l.credit })} />
                </Field>
                <Field label="Credit">
                  <Input type="number" min={0} step="any" value={l.credit} onChange={(_, d) => setLine(i, { credit: d.value, debit: d.value ? '' : l.debit })} />
                </Field>
                <LookupCombobox lookup="currencies" label="Currency" required value={l.currency || null} validationMessage={le.currency}
                  onChange={(id) => setLine(i, { currency: id ?? '' })} />
                <AccountTypeDropdown label="Offset account type" value={l.offsetAccountType} allowNone
                  onChange={(v) => setLine(i, { offsetAccountType: v, offsetAccount: '' })} />
                {l.offsetAccountType ? (
                  <LookupCombobox key={`off-${l.offsetAccountType}`} lookup={accountLookup[l.offsetAccountType]} label="Offset account"
                    value={l.offsetAccount || null} validationMessage={le.offsetAccountType}
                    onChange={(id) => setLine(i, { offsetAccount: id ?? '' })} />
                ) : <span />}
                <Field label="Voucher" hint="Leave empty to let D365 assign">
                  <Input value={l.voucher} maxLength={20} onChange={(_, d) => setLine(i, { voucher: d.value })} />
                </Field>
              </div>
            </div>
          );
        })}
        <div>
          <Button icon={<Add20Regular />} onClick={() => setLines((ls) => [...ls, emptyJournalLine(ls[ls.length - 1])])} disabled={lines.length >= 500}>
            Add line
          </Button>
        </div>
      </Card>

      <JournalTotalsBar lines={lines} />
      {unbalanced && <Caption1 className={s.muted}>The journal can be saved out of balance, but D365 won’t post it until debits equal credits.</Caption1>}

      <div className={s.lineHead}>
        <Button appearance="primary" icon={<Send20Regular />} onClick={submit} disabled={create.isPending || !company}>
          {create.isPending ? 'Creating in D365…' : 'Create journal'}
        </Button>
        <Button onClick={() => navigate('/general-journals')} disabled={create.isPending}>Cancel</Button>
      </div>
    </div>
  );
}

export function JournalDetailPage() {
  const s = useStyles();
  const api = useApi();
  const { number = '' } = useParams();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);
  const j = useQuery({ queryKey: ['journals', tenantId, company, 'detail', number], queryFn: () => getJournal(api, number) });
  const d = j.data;
  return (
    <DetailFrame crumbs={<Crumbs parent="General journals" parentPath="/general-journals" current={number} />}
      loading={j.isLoading} error={j.error} loadingLabel="Loading journal from D365…">
      {d && (
        <>
          <DetailHeader title={d.journalBatchNumber} badges={<PostedBadge posted={d.posted} />} d365Url={d.d365Url} />
          <Card className={s.card}>
            <Facts items={[['Company', d.company], ['Journal name', d.journalName], ['Description', d.description], ['Lines', d.lines.length]]} />
          </Card>
          <Card className={s.card}>
            <div className={s.scroll}>
              <Table size="small" aria-label="Journal lines" className={s.table}>
                <TableHeader>
                  <TableRow>
                    <TableHeaderCell>Date</TableHeaderCell>
                    <TableHeaderCell>Voucher</TableHeaderCell>
                    <TableHeaderCell>Account</TableHeaderCell>
                    <TableHeaderCell>Description</TableHeaderCell>
                    <TableHeaderCell className={s.num}>Debit</TableHeaderCell>
                    <TableHeaderCell className={s.num}>Credit</TableHeaderCell>
                    <TableHeaderCell>Currency</TableHeaderCell>
                    <TableHeaderCell>Offset account</TableHeaderCell>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {d.lines.map((l) => (
                    <TableRow key={l.lineNumber}>
                      <TableCell>{formatDate(l.date)}</TableCell>
                      <TableCell className={s.mono}>{l.voucher}</TableCell>
                      <TableCell className={s.mono}>{accountTypeLabel(l.accountType)} · {l.account}</TableCell>
                      <TableCell>{l.text}</TableCell>
                      <TableCell className={s.num}>{l.debit ? formatAmount(l.debit) : ''}</TableCell>
                      <TableCell className={s.num}>{l.credit ? formatAmount(l.credit) : ''}</TableCell>
                      <TableCell>{l.currency}</TableCell>
                      <TableCell className={s.mono}>{l.offsetAccount ? `${accountTypeLabel(l.offsetAccountType)} · ${l.offsetAccount}` : ''}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
            <div aria-label="Totals">
              {d.totals.map((t) => (
                <div key={t.currency} className={s.total}>
                  {t.currency} · Debit {formatAmount(t.debit)} · Credit {formatAmount(t.credit)} ·{' '}
                  <span className={t.balance === 0 ? s.ok : s.off}>{t.balance === 0 ? 'Balanced' : `Balance ${formatAmount(t.balance)}`}</span>
                </div>
              ))}
            </div>
          </Card>
        </>
      )}
    </DetailFrame>
  );
}
