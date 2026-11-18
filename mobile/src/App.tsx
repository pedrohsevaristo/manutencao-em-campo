import { StatusBar } from 'expo-status-bar';
import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator, KeyboardAvoidingView, Platform, Pressable, ScrollView,
  StyleSheet, Text, TextInput, View,
} from 'react-native';
import {
  actionLabels, ApiError, completeTicket, getTicket, listTickets, login,
  priorityLabels, startTicket, statusLabels,
} from './api';
import type { Details, Status, Ticket } from './api';

type Session = Awaited<ReturnType<typeof login>>;

const filters: { value: Status | null; label: string }[] = [
  { value: null, label: 'Todos' },
  { value: 'Open', label: 'Abertos' },
  { value: 'InProgress', label: 'Em atendimento' },
  { value: 'Completed', label: 'Concluídos' },
];

const date = new Intl.DateTimeFormat('pt-BR', {
  dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Sao_Paulo',
});

function Button({ label, onPress, secondary = false, disabled = false }: {
  label: string; onPress: () => void; secondary?: boolean; disabled?: boolean;
}) {
  return (
    <Pressable accessibilityRole="button" disabled={disabled} onPress={onPress}
      style={[styles.button, secondary && styles.secondary, disabled && styles.disabled]}>
      <Text style={[styles.buttonLabel, secondary && styles.secondaryLabel]}>{label}</Text>
    </Pressable>
  );
}

function StatusBadge({ status }: { status: Status }) {
  return (
    <Text style={[styles.badge, status === 'Completed' && styles.completed, status === 'InProgress' && styles.inProgress]}>
      {statusLabels[status]}
    </Text>
  );
}

function LoginScreen({ onLogin }: { onLogin: (session: Session) => void }) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit() {
    if (busy) return;
    if (!email.trim() || !password) {
      setError('Informe seu e-mail e sua senha.');
      return;
    }
    setBusy(true);
    setError('');
    try {
      onLogin(await login(email.trim(), password));
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <View style={styles.loginCard}>
      <Text accessibilityRole="header" style={styles.title}>Acesso do técnico</Text>
      <Text style={styles.muted}>Entre para acompanhar os serviços atribuídos a você.</Text>
      <Text style={styles.label}>E-mail</Text>
      <TextInput accessibilityLabel="E-mail" style={styles.input} value={email} onChangeText={setEmail}
        autoCapitalize="none" autoComplete="email" keyboardType="email-address" />
      <Text style={styles.label}>Senha</Text>
      <TextInput accessibilityLabel="Senha" style={styles.input} secureTextEntry value={password}
        onChangeText={setPassword} autoComplete="current-password" onSubmitEditing={submit} />
      {!!error && <Text accessibilityRole="alert" style={styles.error}>{error}</Text>}
      <Button label={busy ? 'Entrando…' : 'Entrar'} onPress={submit} disabled={busy} />
    </View>
  );
}

function TechnicianWorkspace({ session, onLogout }: { session: Session; onLogout: (reason?: string) => void }) {
  const [status, setStatus] = useState<Status | null>(null);
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<Ticket[]>([]);
  const [total, setTotal] = useState(0);
  const [selected, setSelected] = useState<number | null>(null);
  const [details, setDetails] = useState<Details | null>(null);
  const [note, setNote] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [refresh, setRefresh] = useState(0);
  const token = session.accessToken;

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError('');
    async function load() {
      try {
        if (selected === null) {
          const result = await listTickets(token, status, page);
          if (active) {
            setItems(result.items);
            setTotal(result.total);
          }
        } else {
          const result = await getTicket(token, selected);
          if (active) setDetails(result);
        }
      } catch (cause) {
        if (!active) return;
        if (cause instanceof ApiError && cause.status === 401) onLogout('Sua sessão terminou. Entre novamente.');
        else setError((cause as Error).message);
      } finally {
        if (active) setLoading(false);
      }
    }
    void load();
    return () => { active = false; };
  }, [selected, status, page, refresh, token, onLogout]);

  function open(id: number | null) {
    setNotice('');
    setNote('');
    setDetails(null);
    setSelected(id);
  }

  async function change(completing: boolean) {
    if (selected === null || saving) return;
    if (completing && note.trim().length < 10) {
      setError('Descreva o serviço realizado em pelo menos 10 caracteres.');
      return;
    }

    setSaving(true);
    setError('');
    setNotice('');
    try {
      if (completing) await completeTicket(token, selected, note.trim());
      else await startTicket(token, selected);
      setNotice(completing ? 'Atendimento concluído.' : 'Atendimento iniciado.');
      setDetails(null);
      setRefresh(value => value + 1);
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 401) onLogout('Sua sessão terminou. Entre novamente.');
      else setError((cause as Error).message);
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <View style={styles.toolbar}>
        <Text style={styles.muted}>{session.name}</Text>
        <Button label="Sair" onPress={() => onLogout()} secondary />
      </View>
      {selected === null ? (
        <>
          <Text accessibilityRole="header" style={styles.title}>Meus chamados</Text>
          <View style={styles.filters}>
            {filters.map(filter => (
              <Pressable key={filter.label} accessibilityRole="button" accessibilityState={{ selected: status === filter.value }}
                disabled={loading} style={[styles.filter, status === filter.value && styles.filterActive]}
                onPress={() => { setStatus(filter.value); setPage(1); }}>
                <Text style={status === filter.value ? styles.filterActiveText : styles.muted}>{filter.label}</Text>
              </Pressable>
            ))}
          </View>
        </>
      ) : (
        <Button label="← Meus chamados" secondary onPress={() => open(null)} disabled={saving} />
      )}
      {loading && <ActivityIndicator color={colors.accent} accessibilityLabel="Carregando chamados" />}
      {!!error && <Text accessibilityRole="alert" style={styles.error}>{error}</Text>}
      {!!notice && <Text accessibilityLiveRegion="polite" style={styles.notice}>{notice}</Text>}
      {!loading && !!error && (
        <Button label="Atualizar" secondary onPress={() => {
          setNotice('');
          setRefresh(value => value + 1);
        }} />
      )}
      {!loading && !error && selected === null && (
        <>
          {items.length === 0 && <Text style={styles.muted}>Nenhum chamado neste filtro.</Text>}
          {items.map(ticket => (
            <Pressable key={ticket.id} accessibilityRole="button" onPress={() => open(ticket.id)} style={styles.card}>
              <View style={styles.cardHeader}>
                <Text style={styles.label}>#{ticket.id} · Prioridade {priorityLabels[ticket.priority].toLowerCase()}</Text>
                <StatusBadge status={ticket.status} />
              </View>
              <Text style={styles.cardTitle}>{ticket.title}</Text>
              <Text style={styles.muted}>{ticket.location}</Text>
            </Pressable>
          ))}
          {total > 10 && (
            <View style={styles.toolbar}>
              <Button label="Anterior" secondary disabled={page <= 1} onPress={() => setPage(page - 1)} />
              <Button label="Próxima" secondary disabled={page * 10 >= total} onPress={() => setPage(page + 1)} />
            </View>
          )}
        </>
      )}
      {!loading && details && selected !== null && (
        <View style={styles.card}>
          <View style={styles.cardHeader}>
            <Text style={styles.label}>Chamado #{details.ticket.id}</Text>
            <StatusBadge status={details.ticket.status} />
          </View>
          <Text accessibilityRole="header" style={styles.title}>{details.ticket.title}</Text>
          <Text style={styles.muted}>{details.ticket.location}</Text>
          <Text style={styles.body}>{details.description}</Text>
          {details.ticket.status === 'Open' && (
            <Button label="Iniciar atendimento" onPress={() => change(false)} disabled={saving} />
          )}
          {details.ticket.status === 'InProgress' && (
            <>
              <Text style={styles.label}>Serviço realizado</Text>
              <TextInput accessibilityLabel="Serviço realizado" style={[styles.input, styles.multiline]} value={note}
                onChangeText={setNote} multiline maxLength={1000} placeholder="Descreva o que foi feito" />
              <Button label="Concluir atendimento" onPress={() => change(true)} disabled={saving} />
            </>
          )}
          <Text accessibilityRole="header" style={styles.cardTitle}>Histórico</Text>
          {details.history.map((entry, index) => (
            <View key={index} style={styles.timeline}>
              <Text style={styles.label}>{actionLabels[entry.action]}</Text>
              <Text style={styles.muted}>{entry.actor} · {date.format(new Date(entry.occurredAt))}</Text>
              {!!entry.note && <Text style={styles.body}>{entry.note}</Text>}
            </View>
          ))}
        </View>
      )}
    </>
  );
}

const colors = { background: '#f6f8f7', paper: '#fff', ink: '#172a26', muted: '#52645f',
  line: '#dfe7e3', accent: '#0c674f', dark: '#132b29' };

export default function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [sessionNotice, setSessionNotice] = useState('');
  const logout = useCallback((reason = '') => {
    setSessionNotice(reason);
    setSession(null);
  }, []);

  return (
    <KeyboardAvoidingView style={styles.screen} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <StatusBar style="light" />
      <View style={styles.header}><Text style={styles.brand}>Manutenção em Campo</Text></View>
      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        <View style={styles.column}>
          {!!sessionNotice && <Text accessibilityRole="alert" style={styles.error}>{sessionNotice}</Text>}
          {session
            ? <TechnicianWorkspace session={session} onLogout={logout} />
            : <LoginScreen onLogin={next => { setSessionNotice(''); setSession(next); }} />}
        </View>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  header: { backgroundColor: colors.dark, paddingTop: Platform.OS === 'web' ? 17 : 44, paddingBottom: 15, paddingHorizontal: 20 },
  brand: { color: '#eff8f3', fontSize: 18, fontWeight: '700', alignSelf: 'center' },
  scroll: { padding: 18, alignItems: 'center' },
  column: { width: '100%', maxWidth: 530, gap: 14 },
  loginCard: { padding: 20, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.paper, borderRadius: 14, gap: 12 },
  title: { fontSize: 27, fontWeight: '700', color: colors.ink },
  muted: { fontSize: 14, color: colors.muted, lineHeight: 20 },
  label: { fontSize: 14, fontWeight: '600', color: colors.ink },
  body: { fontSize: 15, color: colors.ink, lineHeight: 23 },
  input: { borderWidth: 1, borderColor: '#bacac1', borderRadius: 8, padding: 11, color: colors.ink, minHeight: 44, fontSize: 16 },
  multiline: { minHeight: 98, textAlignVertical: 'top' },
  button: { borderWidth: 1, borderColor: colors.accent, backgroundColor: colors.accent, alignItems: 'center', justifyContent: 'center', borderRadius: 8, padding: 10, minHeight: 44 },
  buttonLabel: { color: '#fff', fontWeight: '700', fontSize: 15 },
  secondary: { backgroundColor: '#fff', borderColor: colors.line },
  secondaryLabel: { color: colors.accent },
  disabled: { opacity: 0.5 },
  error: { color: '#973629', backgroundColor: '#fce9e5', borderRadius: 7, padding: 10 },
  notice: { color: '#075940', backgroundColor: '#dff4e8', borderRadius: 7, padding: 10 },
  toolbar: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  filters: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  filter: { paddingHorizontal: 10, paddingVertical: 8, borderRadius: 16, backgroundColor: '#edf2ee' },
  filterActive: { backgroundColor: colors.accent },
  filterActiveText: { color: '#fff', fontWeight: '600', lineHeight: 20 },
  card: { padding: 15, borderColor: colors.line, borderWidth: 1, borderRadius: 10, backgroundColor: colors.paper, gap: 10 },
  cardHeader: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  cardTitle: { color: colors.ink, fontWeight: '700', fontSize: 18 },
  badge: { color: '#364d47', backgroundColor: '#edf2ee', borderRadius: 12, paddingVertical: 4, paddingHorizontal: 8, fontSize: 12, fontWeight: '700' },
  completed: { color: '#075940', backgroundColor: '#dff4e8' },
  inProgress: { color: '#704c06', backgroundColor: '#fff1cd' },
  timeline: { paddingLeft: 10, gap: 4, borderLeftWidth: 2, borderColor: '#bdd9ca' },
});
