import { Body1, MessageBar, MessageBarBody, Title2 } from '@fluentui/react-components';
import { useTranslation } from 'react-i18next';

export function PlaceholderPage({ title, step }: { title: string; step: number }) {
  const { t } = useTranslation();
  return (
    <>
      <Title2 as="h1">{t('placeholder.heading', { title })}</Title2>
      <MessageBar intent="info" style={{ marginTop: 16 }}>
        <MessageBarBody>
          <Body1>{t('placeholder.body', { step })}</Body1>
        </MessageBarBody>
      </MessageBar>
    </>
  );
}
