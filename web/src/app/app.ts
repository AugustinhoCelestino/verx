import { Component, effect, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, Lancamento, SaldoDiario, Tipo } from './api.service';

@Component({
  selector: 'app-root',
  imports: [FormsModule, CurrencyPipe, DatePipe],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly api = inject(ApiService);

  protected data = new Date().toISOString().slice(0, 10);
  protected tipo: Tipo = 1;
  protected valor: number | null = null;
  protected descricao = '';

  protected readonly usuario = this.api.usuario;
  protected readonly lancamentos = signal<Lancamento[]>([]);
  protected readonly saldo = signal<SaldoDiario | null>(null);
  protected readonly erro = signal('');
  protected readonly aviso = signal('');

  protected modoCadastro = false;
  protected nome = '';
  protected email = '';
  protected senha = '';
  protected readonly erroLogin = signal('');

  constructor() {
    // Ao logar (ou restaurar a sessão) carrega os dados; ao sair, limpa a tela.
    effect(() => {
      if (this.usuario()) void this.atualizar();
      else {
        this.lancamentos.set([]);
        this.saldo.set(null);
        this.aviso.set('');
      }
    });
  }

  protected async entrar() {
    this.erroLogin.set('');
    try {
      if (this.modoCadastro) await this.api.registrarUsuario(this.nome, this.email, this.senha);
      else await this.api.login(this.email, this.senha);
      this.senha = '';
    } catch (e: any) {
      this.erroLogin.set(e?.error?.title ?? 'Não foi possível conectar ao serviço de autenticação.');
    }
  }

  protected sair() {
    this.api.logout();
  }

  protected alternarModo() {
    this.modoCadastro = !this.modoCadastro;
    this.erroLogin.set('');
  }

  protected async registrar() {
    this.erro.set('');
    if (!this.valor || this.valor <= 0) {
      this.erro.set('Informe um valor maior que zero.');
      return;
    }
    try {
      const antes = this.saldo()?.quantidadeLancamentos ?? 0;
      await this.api.registrar(this.tipo, this.valor, this.data, this.descricao);
      this.valor = null;
      this.descricao = '';
      this.aviso.set('Lançamento registrado. O consolidado é atualizado de forma assíncrona.');
      await this.atualizar();
      void this.aguardarConsolidado(antes);
    } catch (e: any) {
      this.erro.set(e?.error?.title ?? 'Não foi possível registrar o lançamento.');
    }
  }

  // O consolidado é eventualmente consistente: reconsulta por alguns segundos até refletir o novo lançamento.
  private async aguardarConsolidado(quantidadeAnterior: number) {
    for (let i = 0; i < 10 && (this.saldo()?.quantidadeLancamentos ?? 0) <= quantidadeAnterior; i++) {
      await new Promise((r) => setTimeout(r, 1000));
      try {
        this.saldo.set(await this.api.consolidado(this.data));
      } catch {
        return;
      }
    }
  }

  protected async atualizar() {
    this.erro.set('');
    // Os dois serviços são independentes: a falha de um não impede a exibição do outro.
    const [l, s] = await Promise.allSettled([this.api.listar(this.data), this.api.consolidado(this.data)]);
    if (l.status === 'fulfilled') this.lancamentos.set(l.value);
    else this.erro.set('Serviço de lançamentos indisponível.');
    if (s.status === 'fulfilled') this.saldo.set(s.value);
    else this.saldo.set(null);
  }
}
