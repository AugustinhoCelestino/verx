import { Injectable, inject } from '@angular/core';
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
}

export interface SaldoDiario {
  data: string;
  totalCreditos: number;
  totalDebitos: number;
  saldo: number;
  quantidadeLancamentos: number;
  atualizadoEm: string | null;
}

const LANCAMENTOS_URL = 'http://localhost:5101';
const CONSOLIDADO_URL = 'http://localhost:5102';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private token?: string;

  // Demo: obtém um token de desenvolvimento. Em produção o login é feito no IdP (OIDC).
  private async headers() {
    if (!this.token) {
      const r = await firstValueFrom(this.http.post<{ access_token: string }>(`${LANCAMENTOS_URL}/dev/token`, {}));
      this.token = r.access_token;
    }
    return { Authorization: `Bearer ${this.token}` };
  }

  async registrar(tipo: Tipo, valor: number, data: string, descricao: string) {
    return firstValueFrom(
      this.http.post<Lancamento>(`${LANCAMENTOS_URL}/lancamentos`, { tipo, valor, data, descricao }, { headers: await this.headers() }),
    );
  }

  async listar(data: string) {
    return firstValueFrom(
      this.http.get<Lancamento[]>(`${LANCAMENTOS_URL}/lancamentos`, { params: { data }, headers: await this.headers() }),
    );
  }

  async consolidado(data: string) {
    return firstValueFrom(
      this.http.get<SaldoDiario>(`${CONSOLIDADO_URL}/consolidado/${data}`, { headers: await this.headers() }),
    );
  }
}
