import { useEffect, useMemo, useState } from 'react';
import {
  Button,
  Caption1,
  Checkbox,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Dropdown,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Option,
  Spinner,
  Switch,
  Tag,
  TagGroup,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { useQuery } from '@tanstack/react-query';
import { companyCode, userFormSchema, type AdminScope, type User, type UserForm } from '@/api/admin';
import { ApiError } from '@/api/client';
import { fieldErrors } from '@/components/notify';

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', rowGap: '12px' },
  companies: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(150px, 1fr))',
    gap: '2px 8px',
    maxHeight: '180px',
    overflowY: 'auto',
    padding: '4px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
  },
  addRow: { display: 'flex', columnGap: '8px', alignItems: 'flex-start' },
  muted: { color: tokens.colorNeutralForeground3 },
});

export interface UserDialogProps {
  scope: AdminScope;
  open: boolean;
  user: User | null; // null = add
  saving: boolean;
  serverError: string | null;
  onCancel: () => void;
  onSubmit: (form: UserForm) => void;
}

const blank: UserForm = { email: '', displayName: '', role: 'User', companies: [], defaultCompany: null, isActive: true };

export function UserDialog({ scope, open, user, saving, serverError, onCancel, onSubmit }: UserDialogProps) {
  const s = useStyles();
  const [form, setForm] = useState<UserForm>(blank);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [manualCode, setManualCode] = useState('');

  useEffect(() => {
    if (!open) return;
    setErrors({});
    setManualCode('');
    setForm(
      user
        ? { email: user.email, displayName: user.displayName, role: user.role, companies: user.companies,
            defaultCompany: user.defaultCompany, isActive: user.isActive }
        : blank,
    );
  }, [open, user]);

  // Allowed companies come live from D365 (DataAreas). If the connection isn't ready, codes can be typed.
  const companiesQuery = useQuery({
    queryKey: [...scope.key, 'companies'],
    queryFn: () => scope.getCompanies(),
    enabled: open,
    staleTime: 5 * 60_000,
    retry: false,
  });

  const options = useMemo(() => {
    const fromD365 = companiesQuery.data ?? [];
    const known = new Set(fromD365.map((c) => c.id));
    const extra = form.companies.filter((c) => !known.has(c)).map((id) => ({ id, name: '' }));
    return [...fromD365, ...extra];
  }, [companiesQuery.data, form.companies]);

  const toggleCompany = (id: string, checked: boolean) =>
    setForm((f) => {
      const companies = checked ? [...new Set([...f.companies, id])].sort() : f.companies.filter((c) => c !== id);
      const defaultCompany =
        f.defaultCompany && companies.includes(f.defaultCompany) ? f.defaultCompany : (companies[0] ?? null);
      return { ...f, companies, defaultCompany };
    });

  const addManual = () => {
    const parsed = companyCode.safeParse(manualCode);
    if (!parsed.success) {
      setErrors((e) => ({ ...e, manual: parsed.error.issues[0]?.message ?? 'Invalid code' }));
      return;
    }
    toggleCompany(parsed.data.toUpperCase(), true);
    setManualCode('');
    setErrors(({ manual: _drop, ...rest }) => rest);
  };

  const submit = () => {
    const parsed = userFormSchema.safeParse(form);
    if (!parsed.success) {
      setErrors(fieldErrors(parsed.error.issues));
      return;
    }
    onSubmit(parsed.data);
  };

  const companiesUnavailable =
    companiesQuery.isError && companiesQuery.error instanceof ApiError ? companiesQuery.error.message : null;

  return (
    <Dialog open={open} onOpenChange={(_, d) => !d.open && onCancel()}>
      <DialogSurface aria-describedby={undefined}>
        <DialogBody>
          <DialogTitle>{user ? `Edit ${user.displayName}` : 'Add user'}</DialogTitle>
          <DialogContent className={s.form}>
            {serverError && (
              <MessageBar intent="error">
                <MessageBarBody>{serverError}</MessageBarBody>
              </MessageBar>
            )}
            <Field label="Microsoft sign-in email" required validationMessage={errors.email}
              hint={user ? undefined : 'The user signs in with this Microsoft account; it is linked on first sign-in.'}>
              <Input type="email" value={form.email} disabled={!!user} onChange={(_, d) => setForm((f) => ({ ...f, email: d.value }))} />
            </Field>
            <Field label="Display name" validationMessage={errors.displayName}>
              <Input value={form.displayName ?? ''} onChange={(_, d) => setForm((f) => ({ ...f, displayName: d.value }))} />
            </Field>
            <Field label="Role">
              <Dropdown
                value={form.role === 'TenantAdmin' ? 'Tenant admin' : 'User'}
                selectedOptions={[form.role]}
                onOptionSelect={(_, d) => setForm((f) => ({ ...f, role: (d.optionValue as UserForm['role']) ?? 'User' }))}
              >
                <Option value="User">User</Option>
                <Option value="TenantAdmin">Tenant admin</Option>
              </Dropdown>
            </Field>

            <Field label="Allowed companies" validationMessage={errors.companies}>
              {companiesQuery.isLoading ? (
                <Spinner size="tiny" label="Loading companies from D365…" />
              ) : (
                <>
                  {companiesUnavailable && (
                    <Caption1 className={s.muted}>Couldn’t load companies from D365 ({companiesUnavailable}). You can type codes below.</Caption1>
                  )}
                  {options.length > 0 && (
                    <div className={s.companies} role="group" aria-label="Allowed companies">
                      {options.map((c) => (
                        <Checkbox key={c.id} checked={form.companies.includes(c.id)}
                          label={c.name ? `${c.id} – ${c.name}` : c.id}
                          onChange={(_, d) => toggleCompany(c.id, d.checked === true)} />
                      ))}
                    </div>
                  )}
                </>
              )}
            </Field>
            <Field validationMessage={errors.manual} label="Add company code">
              <div className={s.addRow}>
                <Input value={manualCode} placeholder="e.g. USMF" onChange={(_, d) => setManualCode(d.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); addManual(); } }} />
                <Button onClick={addManual}>Add</Button>
              </div>
            </Field>
            {form.companies.length > 0 && (
              <TagGroup aria-label="Selected companies" onDismiss={(_, d) => toggleCompany(String(d.value), false)}>
                {form.companies.map((c) => (
                  <Tag key={c} value={c} dismissible dismissIcon={{ 'aria-label': `Remove ${c}` }}>{c}</Tag>
                ))}
              </TagGroup>
            )}

            <Field label="Default company" validationMessage={errors.defaultCompany}>
              <Dropdown
                disabled={form.companies.length === 0}
                placeholder={form.companies.length === 0 ? 'Select companies first' : 'Choose'}
                value={form.defaultCompany ?? ''}
                selectedOptions={form.defaultCompany ? [form.defaultCompany] : []}
                onOptionSelect={(_, d) => setForm((f) => ({ ...f, defaultCompany: d.optionValue ?? null }))}
              >
                {form.companies.map((c) => (
                  <Option key={c} value={c}>{c}</Option>
                ))}
              </Dropdown>
            </Field>

            {user && (
              <Switch checked={form.isActive} label={form.isActive ? 'Active' : 'Deactivated (cannot sign in)'}
                onChange={(_, d) => setForm((f) => ({ ...f, isActive: d.checked }))} />
            )}
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onCancel} disabled={saving}>Cancel</Button>
            <Button appearance="primary" onClick={submit} disabled={saving}>{saving ? 'Saving…' : user ? 'Save' : 'Add user'}</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
