import { chromium } from '@playwright/test';
import fs from 'node:fs';
const base = process.env.REVIEW_URL || 'http://127.0.0.1:5179';
const phase = process.argv[2] ?? 'after';
const folder = `../docs/orken-experience/screenshots/${phase}`;
fs.mkdirSync(folder, { recursive: true });
const browser = await chromium.launch({ headless: true, channel: "msedge" });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
page.setDefaultTimeout(90000);
page.on('pageerror', error => console.log('Page error:', error.message));
await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' || new URL(route.request().url()).hostname === 'localhost' ? route.continue() : route.abort());
// Deliberately empty API fixture. No production credentials, records or requests.
await page.route('http://localhost:5000/api/**', route => {
  const path = new URL(route.request().url()).pathname;
  const session = { userId: 'qa', tenantId: 'qa', name: 'Revisão local', login: 'qa', role: 'diretoria', activeModules: ['varejo'], companyName: 'Ambiente de teste', storeIds: [], type: 'tenant' };
  const body = path.endsWith('/auth/me') ? session : path.includes('customers/paged') ? {items: [], totalCount: 0, totalPages: 1, page: 1, pageSize: 25} : [];
  return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
});
await page.goto(base + '/login', {waitUntil: 'domcontentloaded'});
await page.getByRole('button', { name: 'Entrar', exact: true }).waitFor();
await page.screenshot({path: `${folder}/login-1440.png`, fullPage: true});
await page.goto(base + '/', {waitUntil: 'domcontentloaded'});
await page.waitForTimeout(500);
await page.screenshot({path: `${folder}/landing-1440.png`, fullPage: true});
await page.evaluate(() => {
  localStorage.setItem('nexo:session', JSON.stringify({userId:'qa',tenantId:'qa', name:'Revisão local',role:'diretoria',modules:['varejo'],companyName:'Ambiente de teste',storeIds:[],type:'tenant'}));
  localStorage.setItem('nexo:access_token', 'local-test-only');
});
await page.goto(base + '/clientes', {waitUntil: 'domcontentloaded'});
await page.getByRole('heading', { name: 'Clientes', exact: true }).waitFor();
await page.waitForTimeout(700);
await page.screenshot({path: `${folder}/clientes-1440.png`, fullPage: true});
await page.setViewportSize({width:390,height:844});
await page.screenshot({path: `${folder}/clientes-390.png`, fullPage: true});
await browser.close();
console.log(`Saved ${phase} comparison captures using explicitly synthetic empty test session.`);



