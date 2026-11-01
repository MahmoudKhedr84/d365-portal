import { Body1, Button, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { useTranslation } from 'react-i18next';
import { CenteredPanel } from '@/components/CenteredPanel';

export function NotAuthorizedPage({ email, onSignOut }: { email: string | null; onSignOut: () => void }) {
  const { t } = useTranslation();
  return (
    <CenteredPanel title={t('notAuthorized.heading')}>
      <MessageBar intent="warning">
        <MessageBarBody>{t('notAuthorized.body', { email: email ?? '' })}</MessageBarBody>
      </MessageBar>
      <Body1>
        <Button appearance="secondary" onClick={onSignOut}>
          {t('notAuthorized.signOut')}
        </Button>
      </Body1>
    </CenteredPanel>
  );
}
