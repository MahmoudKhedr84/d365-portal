import {
  Body1,
  Breadcrumb,
  BreadcrumbButton,
  BreadcrumbDivider,
  BreadcrumbItem,
  Button,
  Caption1,
  Card,
  MessageBar,
  MessageBarBody,
  Spinner,
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
import { Open20Regular } from '@fluentui/react-icons';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import { useApi } from '@/api/ApiProvider';
import { getPurchaseRequisition } from '@/api/purchaseRequisitions';
import { errorMessage } from '@/components/notify';
import { useSessionStore } from '@/state/sessionStore';
import { PrStatusBadge, formatAmount, formatDate } from './shared';

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', rowGap: '16px' },
  head: { display: 'flex', alignItems: 'center', columnGap: '12px', flexWrap: 'wrap', rowGap: '8px' },
  spacer: { flexGrow: 1 },
  card: { padding: '20px', rowGap: '12px' },
  facts: {
    display: 'grid',
    gap: '12px 24px',
    gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
  },
  fact: { display: 'flex', flexDirection: 'column', rowGap: '2px' },
  label: { color: tokens.colorNeutralForeground3 },
  scroll: { overflowX: 'auto' },
  table: { minWidth: '900px' },
  mono: { fontFamily: tokens.fontFamilyMonospace },
  num: { textAlign: 'end', fontVariantNumeric: 'tabular-nums' },
  totals: { display: 'flex', justifyContent: 'flex-end', columnGap: '16px', fontWeight: tokens.fontWeightSemibold },
});

export function PurchaseRequisitionDetailPage() {
  const s = useStyles();
  const api = useApi();
  const navigate = useNavigate();
  const { number = '' } = useParams();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);
  const pr = useQuery({ queryKey: ['pr', tenantId, company, 'detail', number], queryFn: () => getPurchaseRequisition(api, number) });

  const crumbs = (
    <Breadcrumb aria-label="Breadcrumb">
      <BreadcrumbItem><BreadcrumbButton onClick={() => navigate('/purchase-requisitions')}>Purchase requisitions</BreadcrumbButton></BreadcrumbItem>
      <BreadcrumbDivider />
      <BreadcrumbItem><BreadcrumbButton current>{number}</BreadcrumbButton></BreadcrumbItem>
    </Breadcrumb>
  );
  if (pr.isLoading) return <div className={s.page}>{crumbs}<Spinner label="Loading requisition from D365…" /></div>;
  if (pr.isError)
    return <div className={s.page}>{crumbs}<MessageBar intent="error"><MessageBarBody>{errorMessage(pr.error)}</MessageBarBody></MessageBar></div>;
  const d = pr.data!;

  const fact = (label: string, value: React.ReactNode) => (
    <div className={s.fact}><Caption1 className={s.label}>{label}</Caption1><Body1>{value || '—'}</Body1></div>
  );

  return (
    <div className={s.page}>
      {crumbs}
      <div className={s.head}>
        <Title2 as="h1">{d.requisitionNumber}</Title2>
        <PrStatusBadge status={d.status} />
        <span className={s.spacer} />
        <Button as="a" href={d.d365Url} target="_blank" rel="noopener noreferrer" icon={<Open20Regular />}>Open in D365</Button>
      </div>

      <Card className={s.card}>
        <div className={s.facts}>
          {fact('Name', d.name)}
          {fact('Purpose', d.purpose)}
          {fact('Preparer', d.preparer)}
          {fact('Requested date', formatDate(d.requestedDate))}
          {fact('Accounting date', formatDate(d.accountingDate))}
        </div>
        {d.justification && fact('Business justification', d.justification)}
      </Card>

      <Card className={s.card}>
        <Subtitle2 as="h2">Lines</Subtitle2>
        <div className={s.scroll}>
          <Table size="small" aria-label="Requisition lines" className={s.table}>
            <TableHeader>
              <TableRow>
                <TableHeaderCell>#</TableHeaderCell>
                <TableHeaderCell>Company</TableHeaderCell>
                <TableHeaderCell>Item / category</TableHeaderCell>
                <TableHeaderCell>Description</TableHeaderCell>
                <TableHeaderCell className={s.num}>Qty</TableHeaderCell>
                <TableHeaderCell>Unit</TableHeaderCell>
                <TableHeaderCell className={s.num}>Unit price</TableHeaderCell>
                <TableHeaderCell className={s.num}>Amount</TableHeaderCell>
                <TableHeaderCell>Vendor</TableHeaderCell>
                <TableHeaderCell>Site / WH</TableHeaderCell>
                <TableHeaderCell>Tax group / item tax</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {d.lines.map((l) => (
                <TableRow key={l.lineNumber}>
                  <TableCell>{l.lineNumber}</TableCell>
                  <TableCell className={s.mono}>{l.buyingLegalEntity}</TableCell>
                  <TableCell className={s.mono}>
                    {l.itemNumber ?? l.procurementCategory}
                    {l.variant && <div>{l.variant}</div>}
                  </TableCell>
                  <TableCell>{l.description ?? l.productName}</TableCell>
                  <TableCell className={s.num}>{l.quantity}</TableCell>
                  <TableCell>{l.unit}</TableCell>
                  <TableCell className={s.num}>{formatAmount(l.unitPrice)}</TableCell>
                  <TableCell className={s.num}>{formatAmount(l.lineAmount, l.currency)}</TableCell>
                  <TableCell className={s.mono}>{l.vendor}</TableCell>
                  <TableCell className={s.mono}>{[l.site, l.warehouse].filter(Boolean).join(' / ')}</TableCell>
                  <TableCell className={s.mono}>{[l.salesTaxGroup, l.itemSalesTaxGroup].filter(Boolean).join(' / ')}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
        <div className={s.totals} aria-label="Totals">
          {d.totals.map((t) => <span key={t.currency}>Total {formatAmount(t.amount, t.currency || null)}</span>)}
        </div>
      </Card>
    </div>
  );
}
