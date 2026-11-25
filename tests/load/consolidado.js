// Teste de carga do NFR: 50 req/s no consolidado, perda máxima de 5%.
// Uso: k6 run -e TOKEN=<jwt> tests/load/consolidado.js
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  scenarios: {
    pico: { executor: 'constant-arrival-rate', rate: 50, timeUnit: '1s', duration: '60s', preAllocatedVUs: 50, maxVUs: 200 },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
    http_req_duration: ['p(95)<500'],
  },
};

export default function () {
  const hoje = new Date().toISOString().slice(0, 10);
  const res = http.get(`${__ENV.BASE_URL || 'http://localhost:5102'}/consolidado/${hoje}`, {
    headers: { Authorization: `Bearer ${__ENV.TOKEN}` },
  });
  check(res, { 'status 200': (r) => r.status === 200 });
}
