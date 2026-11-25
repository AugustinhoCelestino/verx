import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export type Tipo = 1 | 2; // 1 = Crédito, 2 = Débito

export interface Lancamento {
  id: string;
  tipo: Tipo;
  valor: number;
  data: string;
  descricao?: string;
  criadoEm: string;
  usuarioNome: string;
}

export interface SaldoDiario {
  data: string;
  totalCreditos: number;
  totalDebitos: number;
  saldo: number;
  quantidadeLancamentos: number;
  atualizadoEm: string | null;
}

export interface Usuario {
  id: string;
  nome: string;
  email: string;
}

interface LoginResponse {
  access_token: string;
  expires_in: number;
  usuario: Usuario;
}

interface Sessao {
  token: string;
  usuario: Usuario;
  expiraEm: number;
}

const LANCAMENTOS_URL = 'http://localhost:5101';
const CONSOLIDADO_URL = 'http://localhost:5102';
const CHAVE_SESSAO = 'verx.sessao';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private sessao?: Sessao;

  // Usuário logado (null = deslogado).
  readonly usuario = signal<Usuario | null>(null);

  constructor() {
    // sessionStorage: a sessão some ao fechar a aba, reduzindo a exposição do token.
    const salvo = sessionStorage.getItem(CHAVE_SESSAO);
    if (salvo) {
      const s = JSON.parse(salvo) as Sessao;
      if (s.expiraEm > Date.now()) {
        this.sessao = s;
        this.usuario.set(s.usuario);
      } else {
        sessionStorage.removeItem(CHAVE_SESSAO);
      }
    }
  }

  async login(email: string, senha: string) {
    this.iniciar(await firstValueFrom(this.http.post<LoginResponse>(`${LANCAMENTOS_URL}/auth/login`, { email, senha })));
  }

  async registrarUsuario(nome: string, email: string, senha: string) {
    this.iniciar(
      await firstValueFrom(this.http.post<LoginResponse>(`${LANCAMENTOS_URL}/auth/registrar`, { nome, email, senha })),
    );
  }

  logout() {
    this.sessao = undefined;
    this.usuario.set(null);
    sessionStorage.removeItem(CHAVE_SESSAO);
  }

  private iniciar(r: LoginResponse) {
    this.sessao = { token: r.access_token, usuario: r.usuario, expiraEm: Date.now() + r.expires_in * 1000 };
    sessionStorage.setItem(CHAVE_SESSAO, JSON.stringify(this.sessao));
    this.usuario.set(r.usuario);
  }

  private headers() {
    return { Authorization: `Bearer ${this.sessao?.token ?? ''}` };
  }

  // Token expirado/inválido: volta para a tela de login.
  private async chamar<T>(req: Promise<T>): Promise<T> {
    try {
      return await req;
    } catch (e: any) {
      if (e?.status === 401) this.logout();
      throw e;
    }
  }

  registrar(tipo: Tipo, valor: number, data: string, descricao: string) {
    return this.chamar(
      firstValueFrom(
        this.http.post<Lancamento>(`${LANCAMENTOS_URL}/lancamentos`, { tipo, valor, data, descricao }, { headers: this.headers() }),
      ),
    );
  }

  listar(data: string) {
    return this.chamar(
      firstValueFrom(this.http.get<Lancamento[]>(`${LANCAMENTOS_URL}/lancamentos`, { params: { data }, headers: this.headers() })),
    );
  }

  consolidado(data: string) {
    return this.chamar(
      firstValueFrom(this.http.get<SaldoDiario>(`${CONSOLIDADO_URL}/consolidado/${data}`, { headers: this.headers() })),
    );
  }
}
