import { Body1, Button } from '@fluentui/react-components';
import { PersonKey20Regular } from '@fluentui/react-icons';
import { useTranslation } from 'react-i18next';
import { CenteredPanel } from '@/components/CenteredPanel';

export function SignInPage({ onSignIn }: { onSignIn: () => void }) {
  const { t } = useTranslation();
  return (
    <CenteredPanel title={t('signIn.heading')}>
      <Body1>{t('signIn.body')}</Body1>
      <Button appearance="primary" size="large" icon={<PersonKey20Regular />} onClick={onSignIn}>
        {t('signIn.button')}
      </Button>
    </CenteredPanel>
  );
}
