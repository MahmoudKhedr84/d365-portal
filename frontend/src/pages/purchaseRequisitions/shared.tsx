import { Badge, type BadgeProps } from '@fluentui/react-components';
import { prStatusLabel } from '@/api/purchaseRequisitions';

const COLORS: Record<string, BadgeProps['color']> = {
  Draft: 'informative',
  InReview: 'warning',
  Approved: 'success',
  Rejected: 'danger',
  Cancelled: 'subtle',
  Closed: 'subtle',
};

export function PrStatusBadge({ status }: { status: string | null | undefined }) {
  return (
    <Badge appearance="tint" color={COLORS[status ?? ''] ?? 'informative'}>
      {prStatusLabel(status)}
    </Badge>
  );
}

export const formatDate = (d: string | null | undefined) => (d ? new Date(`${d}T00:00:00`).toLocaleDateString() : '—');

export const formatAmount = (n: number, currency?: string | null) =>
  `${n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}${currency ? ` ${currency}` : ''}`;
