import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiService } from './api.service';

const LOGIN = { access_token: 'tok-123', expires_in: 3600, usuario: { id: 'u1', nome: 'Ana', email: 'ana@verx.com' } };

describe('ApiService', () => {
  let http: HttpTestingController;

  function criar() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(ApiService);
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => http.verify());

  it('login guarda a sessão e envia o Bearer nas chamadas seguintes', async () => {
    const api = criar();
    const login = api.login('ana@verx.com', 'senha');
    http.expectOne('http://localhost:5101/auth/login').flush(LOGIN);
    await login;
    expect(api.usuario()?.nome).toBe('Ana');

    const saldo = api.consolidado('2026-11-24');
    const req = http.expectOne('http://localhost:5102/consolidado/2026-11-24');
    expect(req.request.headers.get('Authorization')).toBe('Bearer tok-123');
    req.flush({});
    await saldo;
  });

  it('resposta 401 encerra a sessão e limpa o sessionStorage', async () => {
    const api = criar();
    const login = api.login('ana@verx.com', 'senha');
    http.expectOne('http://localhost:5101/auth/login').flush(LOGIN);
    await login;

    const lista = api.listar('2026-11-24');
    http.expectOne((r) => r.url === 'http://localhost:5101/lancamentos').flush({}, { status: 401, statusText: 'Unauthorized' });
    await expect(lista).rejects.toMatchObject({ status: 401 });

    expect(api.usuario()).toBeNull();
    expect(sessionStorage.getItem('verx.sessao')).toBeNull();
  });

  it('403 (sem perfil admin) não derruba a sessão', async () => {
    const api = criar();
    const login = api.login('ana@verx.com', 'senha');
    http.expectOne('http://localhost:5101/auth/login').flush(LOGIN);
    await login;

    const logs = api.logs('lancamentos', 'Information', '', 50);
    http.expectOne((r) => r.url === 'http://localhost:5101/logs').flush({}, { status: 403, statusText: 'Forbidden' });
    await expect(logs).rejects.toMatchObject({ status: 403 });

    expect(api.usuario()?.nome).toBe('Ana');
  });

  it('logs de cada serviço vão para a API correta e a busca vazia não é enviada', async () => {
    const api = criar();
    const p = api.logs('consolidado', 'Error', '   ', 10);
    const req = http.expectOne((r) => r.url === 'http://localhost:5102/logs');
    expect(req.request.params.get('nivel')).toBe('Error');
    expect(req.request.params.has('busca')).toBe(false);
    req.flush([]);
    await p;
  });

  it('restaura a sessão válida e descarta a expirada', () => {
    const sessao = { token: 't', usuario: LOGIN.usuario, expiraEm: Date.now() + 60_000 };
    sessionStorage.setItem('verx.sessao', JSON.stringify(sessao));
    expect(criar().usuario()?.email).toBe('ana@verx.com');

    TestBed.resetTestingModule();
    sessionStorage.setItem('verx.sessao', JSON.stringify({ ...sessao, expiraEm: Date.now() - 1 }));
    expect(criar().usuario()).toBeNull();
    expect(sessionStorage.getItem('verx.sessao')).toBeNull();
  });
});
