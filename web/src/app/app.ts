import { Component, effect, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, Lancamento, LogEntry, NivelLog, SaldoDiario, Servico, Tipo } from './api.service';

@Component({
  selector: 'app-root',
  imports: [FormsModule, CurrencyPipe, DatePipe],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly api = inject(ApiService);

  protected data = new Date().toLocaleDateString('sv-SE'); // yyyy-MM-dd no fuso local
  protected tipo: Tipo = 1;
  protected valor: number | null = null;
  protected valorTexto = '';
  protected descricao = '';

  protected readonly usuario = this.api.usuario;
  protected readonly lancamentos = signal<Lancamento[]>([]);
  protected readonly saldo = signal<SaldoDiario | null>(null);
  protected readonly erro = signal('');
  protected readonly toast = signal('');
  private toastTimer?: ReturnType<typeof setTimeout>;

  protected readonly aba = signal<'lancamentos' | 'logs'>('lancamentos');
  protected readonly niveis: NivelLog[] = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'];
  protected servicoLog: Servico = 'lancamentos';
  protected nivelLog: NivelLog = 'Information';
  protected buscaLog = '';
  protected limiteLog = 100;
  protected readonly logs = signal<LogEntry[]>([]);
  protected readonly erroLogs = signal('');

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
        this.logs.set([]);
        this.aba.set('lancamentos');
        this.fecharToast();
      }
    });
  }

  protected abrir(aba: 'lancamentos' | 'logs') {
    this.aba.set(aba);
    if (aba === 'logs') void this.consultarLogs();
  }

  protected async consultarLogs() {
    this.erroLogs.set('');
    try {
      this.logs.set(await this.api.logs(this.servicoLog, this.nivelLog, this.buscaLog, this.limiteLog));
    } catch (e: any) {
      this.logs.set([]);
      if (e?.status !== 401) this.erroLogs.set('Não foi possível consultar os logs. Verifique se o serviço está no ar.');
    }
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

  // Máscara monetária (pt-BR): os dígitos digitados entram como centavos, ex.: 12345 -> 123,45.
  protected aoDigitarValor(e: Event) {
    const campo = e.target as HTMLInputElement;
    const digitos = campo.value.replace(/\D/g, '').replace(/^0+/, '').slice(0, 13);
    this.valor = digitos ? Number(digitos) / 100 : null;
    this.valorTexto = this.valor === null ? '' : this.valor.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    campo.value = this.valorTexto;
  }

  protected sair() {
    this.api.logout();
  }

  private mostrarToast(mensagem: string) {
    clearTimeout(this.toastTimer);
    this.toast.set(mensagem);
    this.toastTimer = setTimeout(() => this.toast.set(''), 5000);
  }

  protected fecharToast() {
    clearTimeout(this.toastTimer);
    this.toast.set('');
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
      this.valorTexto = '';
      this.descricao = '';
      this.mostrarToast('Lançamento registrado. O consolidado é atualizado de forma assíncrona.');
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
