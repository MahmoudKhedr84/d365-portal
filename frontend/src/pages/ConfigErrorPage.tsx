import { Body1 } from '@fluentui/react-components';
import { useTranslation } from 'react-i18next';
import { CenteredPanel } from '@/components/CenteredPanel';

export function ConfigErrorPage({ errors }: { errors: string[] }) {
  const { t } = useTranslation();
  return (
    <CenteredPanel title={t('config.heading')}>
      <Body1>{t('config.body')}</Body1>
      <ul>
        {errors.map((e) => (
          <li key={e}>
            <code>{e}</code>
          </li>
        ))}
      </ul>
    </CenteredPanel>
  );
}
