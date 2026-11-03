import type { ReactNode } from 'react';
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
import { Edit20Regular, Open20Regular } from '@fluentui/react-icons';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import { useApi } from '@/api/ApiProvider';
import { getPurchaseOrder } from '@/api/purchaseOrders';
import { errorMessage } from '@/components/notify';
import { formatAmount, formatDate } from '@/pages/purchaseRequisitions/shared';
import { useSessionStore } from '@/state/sessionStore';
import { PoStatusBadges } from './PurchaseOrdersPage';

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', rowGap: '16px' },
  head: { display: 'flex', alignItems: 'center', columnGap: '12px', flexWrap: 'wrap', rowGap: '8px' },
  spacer: { flexGrow: 1 },
  card: { padding: '20px', rowGap: '12px' },
  facts: { display: 'grid', gap: '12px 24px', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))' },
  fact: { display: 'flex', flexDirection: 'column', rowGap: '2px' },
  label: { color: tokens.colorNeutralForeground3 },
  scroll: { overflowX: 'auto' },
  table: { minWidth: '900px' },
  mono: { fontFamily: tokens.fontFamilyMonospace },
  num: { textAlign: 'end', fontVariantNumeric: 'tabular-nums' },
  total: { display: 'flex', justifyContent: 'flex-end', fontWeight: tokens.fontWeightSemibold },
});

export function PurchaseOrderDetailPage() {
  const s = useStyles();
  const api = useApi();
  const navigate = useNavigate();
  const { number = '' } = useParams();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);
  const po = useQuery({ queryKey: ['po', tenantId, company, 'detail', number], queryFn: () => getPurchaseOrder(api, number) });

  const crumbs = (
    <Breadcrumb aria-label="Breadcrumb">
      <BreadcrumbItem><BreadcrumbButton onClick={() => navigate('/purchase-orders')}>Purchase orders</BreadcrumbButton></BreadcrumbItem>
      <BreadcrumbDivider />
      <BreadcrumbItem><BreadcrumbButton current>{number}</BreadcrumbButton></BreadcrumbItem>
    </Breadcrumb>
  );
  if (po.isLoading) return <div className={s.page}>{crumbs}<Spinner label="Loading purchase order from D365…" /></div>;
  if (po.isError)
    return <div className={s.page}>{crumbs}<MessageBar intent="error"><MessageBarBody>{errorMessage(po.error)}</MessageBarBody></MessageBar></div>;
  const d = po.data!;
  const fact = (label: string, value: ReactNode) => (
    <div className={s.fact}><Caption1 className={s.label}>{label}</Caption1><Body1>{value || '—'}</Body1></div>
  );

  return (
    <div className={s.page}>
      {crumbs}
      <div className={s.head}>
        <Title2 as="h1">{d.purchaseOrderNumber}</Title2>
        <PoStatusBadges status={d.status} approval={d.approvalStatus} />
        <span className={s.spacer} />
        {d.canEdit && (
          <Button appearance="primary" icon={<Edit20Regular />}
            onClick={() => navigate(`/purchase-orders/${encodeURIComponent(d.purchaseOrderNumber)}/edit`)}>Edit</Button>
        )}
        <Button as="a" href={d.d365Url} target="_blank" rel="noopener noreferrer" icon={<Open20Regular />}>Open in D365</Button>
      </div>

      <Card className={s.card}>
        <div className={s.facts}>
          {fact('Company', d.company)}
          {fact('Vendor', [d.vendor, d.vendorName].filter(Boolean).join(' · '))}
          {fact('Currency', d.currency)}
          {fact('Requested receipt', formatDate(d.requestedDeliveryDate))}
          {fact('Accounting date', formatDate(d.accountingDate))}
          {fact('Site / warehouse', [d.site, d.warehouse].filter(Boolean).join(' / '))}
          {fact('Vendor reference', d.vendorReference)}
          {fact('Requester', d.requester)}
          {fact('Sales tax group', d.salesTaxGroup)}
        </div>
      </Card>

      <Card className={s.card}>
        <Subtitle2 as="h2">Lines</Subtitle2>
        <div className={s.scroll}>
          <Table size="small" aria-label="Purchase order lines" className={s.table}>
            <TableHeader>
              <TableRow>
                <TableHeaderCell>#</TableHeaderCell>
                <TableHeaderCell>Item / category</TableHeaderCell>
                <TableHeaderCell>Description</TableHeaderCell>
                <TableHeaderCell className={s.num}>Qty</TableHeaderCell>
                <TableHeaderCell>Unit</TableHeaderCell>
                <TableHeaderCell className={s.num}>Unit price</TableHeaderCell>
                <TableHeaderCell className={s.num}>Amount</TableHeaderCell>
                <TableHeaderCell>Site / WH / location / status</TableHeaderCell>
                <TableHeaderCell>Tax group / item tax</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {d.lines.map((l) => (
                <TableRow key={l.lineNumber}>
                  <TableCell>{l.lineNumber}</TableCell>
                  <TableCell className={s.mono}>
                    {l.itemNumber ?? l.procurementCategory}
                    {l.variant && <div>{l.variant}</div>}
                  </TableCell>
                  <TableCell>{l.description}</TableCell>
                  <TableCell className={s.num}>{l.quantity}</TableCell>
                  <TableCell>{l.unit}</TableCell>
                  <TableCell className={s.num}>{formatAmount(l.unitPrice)}</TableCell>
                  <TableCell className={s.num}>{formatAmount(l.lineAmount)}</TableCell>
                  <TableCell className={s.mono}>{[l.site, l.warehouse, l.location, l.inventoryStatus].filter(Boolean).join(' / ')}</TableCell>
                  <TableCell className={s.mono}>{[l.salesTaxGroup, l.itemSalesTaxGroup].filter(Boolean).join(' / ')}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
        <div className={s.total} aria-label="Total">Total {formatAmount(d.total, d.currency)}</div>
      </Card>
    </div>
  );
}
