const base = (process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5180').replace(/\/$/, '');

export type Status = 'Open' | 'InProgress' | 'Completed';
export type Priority = 'Low' | 'Normal' | 'High';
type Action = 'Created' | 'Assigned' | 'Started' | 'Completed';

export interface Ticket {
  id: number;
  title: string;
  location: string;
  priority: Priority;
  status: Status;
  createdAt: string;
  assignedToName: string | null;
}

export interface Details {
  ticket: Ticket;
  description: string;
  history: { action: Action; actor: string; note: string | null; occurredAt: string }[];
}

interface Page {
  items: Ticket[];
  page: number;
  pageSize: number;
  total: number;
}

export const statusLabels: Record<Status, string> = {
  Open: 'Aberto',
  InProgress: 'Em atendimento',
  Completed: 'Concluído',
};

export const priorityLabels: Record<Priority, string> = {
  Low: 'Baixa',
  Normal: 'Normal',
  High: 'Alta',
};

export const actionLabels: Record<Action, string> = {
  Created: 'Chamado registrado',
  Assigned: 'Responsável atribuído',
  Started: 'Atendimento iniciado',
  Completed: 'Atendimento concluído',
};

export class ApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message);
  }
}

async function request<T>(path: string, token?: string, method = 'GET', body?: object): Promise<T> {
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body) headers['Content-Type'] = 'application/json';

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 10000);
  try {
    const response = await fetch(`${base}${path}`, {
      method,
      headers,
      body: body ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    });
    if (response.status === 204) return undefined as T;
    if (!response.ok) {
      const fallback: Record<number, string> = {
        401: token ? 'Sua sessão terminou. Entre novamente.' : 'E-mail ou senha inválidos.',
        403: 'Sua conta não tem acesso a essa operação.',
        404: 'O chamado já não está disponível para sua conta.',
        429: 'Muitas tentativas. Aguarde um minuto e tente novamente.',
      };
      const problem = await response.json().catch(() => null) as { title?: string } | null;
      const message = fallback[response.status]
        ?? (response.status === 409 ? problem?.title : null)
        ?? 'Não foi possível concluir a solicitação. Tente novamente.';
      throw new ApiError(response.status, message);
    }
    return await response.json() as T;
  } catch (error) {
    if (error instanceof ApiError) throw error;
    throw new Error('Não foi possível conectar ao servidor. Verifique a conexão e tente novamente.');
  } finally {
    clearTimeout(timer);
  }
}

export async function login(email: string, password: string) {
  const { accessToken } = await request<{ accessToken: string }>('/api/session', undefined, 'POST', { email, password });
  const user = await request<{ name: string; role: string }>('/api/session', accessToken);
  if (user.role !== 'Technician') throw new Error('Use uma conta de técnico para entrar no app.');
  return { accessToken, name: user.name };
}

export function listTickets(token: string, status: Status | null, page: number) {
  const filter = status ? `&status=${status}` : '';
  return request<Page>(`/api/chamados?page=${page}&pageSize=10${filter}`, token);
}

export const getTicket = (token: string, id: number) => request<Details>(`/api/chamados/${id}`, token);
export const startTicket = (token: string, id: number) => request<void>(`/api/chamados/${id}/inicio`, token, 'POST');
export const completeTicket = (token: string, id: number, note: string) =>
  request<void>(`/api/chamados/${id}/conclusao`, token, 'POST', { note });
