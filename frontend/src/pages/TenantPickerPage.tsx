import { Body1, Button, Caption1, makeStyles, tokens } from '@fluentui/react-components';
import { Building20Regular } from '@fluentui/react-icons';
import { useTranslation } from 'react-i18next';
import type { Membership } from '@/api/session';
import { CenteredPanel } from '@/components/CenteredPanel';

const useStyles = makeStyles({
  list: { listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', rowGap: '8px' },
  item: {
    display: 'flex',
    alignItems: 'center',
    columnGap: '12px',
    padding: '10px 12px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
  },
  text: { display: 'flex', flexDirection: 'column', flexGrow: 1, minWidth: 0 },
});

export function TenantPickerPage({
  memberships,
  onSelect,
}: {
  memberships: Membership[];
  onSelect: (tenantId: string) => void;
}) {
  const { t } = useTranslation();
  const s = useStyles();
  return (
    <CenteredPanel title={t('tenantPicker.heading')}>
      <Body1>{t('tenantPicker.body')}</Body1>
      <ul className={s.list}>
        {memberships.map((m) => (
          <li key={m.tenantId} className={s.item}>
            <Building20Regular aria-hidden />
            <div className={s.text}>
              <Body1>
                <strong>{m.tenantName}</strong>
              </Body1>
              <Caption1>{t('tenantPicker.role', { role: t(`roles.${m.role}`) })}</Caption1>
            </div>
            <Button appearance="primary" onClick={() => onSelect(m.tenantId)} aria-label={`${t('tenantPicker.open')} ${m.tenantName}`}>
              {t('tenantPicker.open')}
            </Button>
          </li>
        ))}
      </ul>
    </CenteredPanel>
  );
}
