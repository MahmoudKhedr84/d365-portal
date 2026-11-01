import { useMemo } from 'react';
import { FluentProvider, Spinner, webLightTheme } from '@fluentui/react-components';
import { InteractionStatus, type PublicClientApplication } from '@azure/msal-browser';
import { MsalProvider, useIsAuthenticated, useMsal } from '@azure/msal-react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import type { AppConfig } from '@/config';
import { ApiProvider } from '@/api/ApiProvider';
import { loginRequest, signOut } from '@/auth/msal';
import { directionFor } from '@/i18n';
import { useSessionStore } from '@/state/sessionStore';
import { SignInPage } from '@/pages/SignInPage';
import { SessionGate } from '@/components/SessionGate';
import { AppToaster } from '@/components/notify';

function AuthGate() {
  const { t } = useTranslation();
  const { instance, inProgress } = useMsal();
  const isAuthenticated = useIsAuthenticated();
  const reset = useSessionStore((s) => s.reset);

  if (inProgress !== InteractionStatus.None && !isAuthenticated) {
    return <Spinner style={{ marginTop: '30vh' }} label={t('app.loading')} />;
  }

  if (!isAuthenticated) {
    return <SignInPage onSignIn={() => void instance.loginRedirect(loginRequest())} />;
  }

  return (
    <SessionGate
      onSignOut={() => {
        reset();
        void signOut();
      }}
    />
  );
}

export function App({ msal, config }: { msal: PublicClientApplication; config: AppConfig }) {
  const { i18n } = useTranslation();
  const queryClient = useMemo(
    () => new QueryClient({ defaultOptions: { queries: { refetchOnWindowFocus: false } } }),
    [],
  );

  return (
    <FluentProvider theme={webLightTheme} dir={directionFor(i18n.language)} style={{ minHeight: '100vh' }}>
      <MsalProvider instance={msal}>
        <QueryClientProvider client={queryClient}>
          <ApiProvider baseUrl={config.apiBaseUrl}>
            <BrowserRouter>
              <AppToaster />
              <AuthGate />
            </BrowserRouter>
          </ApiProvider>
        </QueryClientProvider>
      </MsalProvider>
    </FluentProvider>
  );
}
