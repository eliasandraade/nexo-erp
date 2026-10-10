import { test, expect, type Page } from '@playwright/test';
const modules = ['varejo', 'restaurante', 'service', 'build'];
const session = { userId:'qa', tenantId:'qa', name:'Revisão local', role:'diretoria', login:'qa', modules, activeModules:modules, companyName:'Ambiente de teste', storeId:'qa-store', storeIds:['qa-store'], type:'tenant' };
const preset = {key:'barbearia',displayName:'Barbearia',labels:{customer:'Cliente',professional:'Profissional',catalogItem:'Serviço',appointment:'Agendamento',order:'Ordem',subject:'Cadastro'},capabilities:{appointments:true,orders:true,packages:true,commissions:false,subjectKind:null}};
const customer = { id:'qa-customer', name:'Registro de teste com nome longo para verificar quebra e leitura em telas pequenas',personType:'Individual',documentNumber:'Documento de teste',phone:null,email:'endereco-de-teste-comprido@example.invalid',isActive:true,addressJson:JSON.stringify({city:'Cidade de teste',state:'CE'}) };
async function prepare(page: Page, options: { populated?: boolean; error?: boolean; role?: string; platform?: boolean } = {}) {
  const auth = {...session, role: options.role || 'diretoria', type: options.platform ? 'platform' : 'tenant'};
  await page.addInitScript(auth => { localStorage.setItem('nexo:session',JSON.stringify(auth));localStorage.setItem('nexo:access_token','qa-only');localStorage.setItem('nexo:setup-dismissed:qa','1');}, auth);
  await page.route('http://localhost:5000/api/**', async route => {
    const url = new URL(route.request().url()), path=url.pathname;
    let body: unknown = [];
    if(path.endsWith('/auth/me')) body=auth;
    else if(path.includes('/stores')) body=[{id:'qa-store',name:'Ambiente de teste'}];
    else if(path.endsWith('/v1/build/dashboard')) body={totalProjects:0};
    else if(path.endsWith('/v1/build/projects')) body={items:[],totalCount:0,page:1,pageSize:50};
    else if(path.endsWith('/service/settings')) body={isConfigured:true,presetKey:'barbearia'};
    else if(path.endsWith('/service/preset')) body=preset;
    else if(path.endsWith('/service/public-booking')) body={isConfigured:true,publicBookingEnabled:false};
    else if(path.includes('/customers/paged')) {
      if(options.error) return route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({message:'Falha de teste'})});
      body={items: options.populated && !url.searchParams.has('search') ? Array.from({length:25},(_,index)=>({...customer,id:'qa-'+index})) : [], totalCount: options.populated ? 50 : 0,totalPages:options.populated ? 2 : 1,page:Number(url.searchParams.get('page')||1),pageSize:25};
    } else if(path.endsWith('/dashboard/summary')) {
      if(options.error) return route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({message:'Falha de teste'})});
      body={totalSales:0,cancelledCount:0,totalRevenue:0,averageTicket:0,topProducts:[],topSellers:[],salesByDay:[],zeroStockCount:0,lowStockCount:0,stockAlerts:[],hasOpenCashSession:false};
    } else if(path.includes('/paged')) body={items:[],totalCount:0,totalPages:1,page:1,pageSize:25};
    return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
  });
}
async function noOverflow(page: Page) { expect(await page.evaluate(()=>document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
for(const width of [1920,1440,1366,1280,1024,768,390,320]) {
  test('Clientes com 25 registros em '+width+'px', async ({page}) => {
    await page.setViewportSize({width,height:900});await prepare(page,{populated:true});
    await page.goto('/clientes');await expect(page.getByRole('heading',{name:'Clientes',exact:true})).toBeVisible();
    await expect(page.getByText('25 nesta página', {exact:false})).toBeVisible();await noOverflow(page);
    await page.screenshot({path:'../docs/orken-experience/screenshots/qa/clientes-'+width+'.png',fullPage:true});
  });
}
test('Busca vazia permite limpar, e paginação continua disponível',async({page})=>{
  await prepare(page,{populated:true});await page.goto('/clientes');
  await page.getByRole('textbox',{name:'Buscar clientes'}).fill('Sem correspondência');
  await expect(page.getByText('Nenhum cliente nesta seleção')).toBeVisible();
  await page.getByRole('button',{name:'Limpar filtros'}).click();
  await expect(page.getByText('25 nesta página',{exact:false})).toBeVisible();
  await expect(page.getByText('Tipo e situação filtram os registros da página atual.')).toBeVisible();
});
test('Falha de clientes não vira lista vazia',async({page})=>{
  await prepare(page,{error:true});await page.goto('/clientes');
  await expect(page.getByRole('alert')).toContainText('Não foi possível carregar os clientes');
  await expect(page.getByRole('button',{name:'Tentar novamente'})).toBeVisible();
  await expect(page.getByText('Nenhum cliente cadastrado')).toHaveCount(0);
});
test('Comando navega por teclado e devolve foco ao fechar',async({page})=>{
  await prepare(page);await page.goto('/clientes');await page.getByRole('button',{name:'Buscar páginas'}).click();
  await page.getByRole('combobox').fill('Produtos');await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/produtos$/);await expect(page.getByRole('button',{name:'Buscar páginas'})).toBeFocused();
  await page.keyboard.press('Control+k');await expect(page.getByRole('dialog')).toBeVisible();await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toHaveCount(0);
});
test('Menu mobile contém foco e fecha com Escape',async({page})=>{
  await page.setViewportSize({width:390,height:844});await prepare(page);await page.goto('/clientes');
  await page.getByRole('button',{name:'Abrir menu'}).click();await expect(page.getByRole('dialog')).toBeVisible();
  for(let index=0;index<25;index++) await page.keyboard.press('Tab');
  expect(await page.evaluate(()=>!!document.activeElement?.closest('[role="dialog"]'))).toBe(true);
  await page.keyboard.press('Escape');await expect(page.getByRole('button',{name:'Abrir menu'})).toBeFocused();
});
test('Estoquista não recebe destinos administrativos no comando',async({page})=>{
  await prepare(page,{role:'estoquista'});await page.goto('/produtos');await page.getByRole('button',{name:'Buscar páginas'}).click();
  await expect(page.getByRole('option',{name:/Usuários/})).toHaveCount(0);await expect(page.getByRole('option',{name:/Clientes/})).toHaveCount(0);await expect(page.getByRole('option',{name:/Estoque/})).toBeVisible();
});
test('Resumo em falha não afirma saúde da operação',async({page})=>{
  await prepare(page,{error:true});await page.goto('/dashboard');await expect(page.getByRole('alert')).toContainText('Não foi possível atualizar a operação');await expect(page.getByText('Estoque saudável.')).toHaveCount(0);
});
test('Login, senha por teclado e movimento reduzido',async({page})=>{
  await page.emulateMedia({reducedMotion:'reduce'});await page.goto('/login');await page.getByLabel('Login ou e-mail').fill('teste');await page.getByLabel('Senha',{exact:true}).fill('somente-teste');
  await page.getByRole('button',{name:'Mostrar senha'}).focus();await page.keyboard.press('Enter');await expect(page.getByLabel('Senha',{exact:true})).toHaveAttribute('type','text');
  await expect(page.getByText('Sistema operando')).toHaveCount(0);await expect(page.getByText('Venda finalizada',{exact:false})).toHaveCount(0);
});
test('Landing: seleção de operação com teclado e mobile',async({page})=>{
  await page.setViewportSize({width:390,height:844});await page.goto('/');await page.getByRole('tab',{name:'Varejo',exact:true}).focus();await page.keyboard.press('ArrowRight');await expect(page.getByRole('tabpanel')).toContainText('Do horário ao recebimento.');await noOverflow(page);
});
test('Service preserva contexto no celular',async({page})=>{
  await page.setViewportSize({width:390,height:844});await prepare(page);await page.goto('/service');await expect(page.getByRole('heading',{name:'Serviços',exact:true})).toBeVisible();await noOverflow(page);
  await page.screenshot({path:'../docs/orken-experience/screenshots/qa/service-390.png',fullPage:true});
});

for (const [route, heading] of [['/clientes/novo','Novo cliente'],['/produtos','Produtos'],['/fornecedores','Fornecedores'],['/build','Obras'],['/service/profissionais','Profissionais'],['/service/catalogo','Catálogo']]) {
 test('Tela operacional no celular: '+route,async({page})=>{
  const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));
  await page.setViewportSize({width:390,height:844});await prepare(page);await page.goto(route);
  await expect(page.getByRole('heading',{name:heading,exact:true})).toBeVisible();await noOverflow(page);expect(errors).toEqual([]);
  await page.screenshot({path:'../docs/orken-experience/screenshots/qa/'+route.replaceAll('/','-')+'-390.png',fullPage:true});
 });
}
test('Plataforma: falha explícita e navegação móvel acessível',async({page})=>{
 await page.setViewportSize({width:390,height:844});await prepare(page,{platform:true});
 await page.route('**/api/platform/**',route=>route.fulfill({status:503,contentType:'application/json',body:'{}'}));
 await page.goto('/platform');await expect(page.getByRole('alert')).toContainText('Não foi possível consultar');await noOverflow(page);
 await page.screenshot({path:'../docs/orken-experience/screenshots/qa/platform-390.png',fullPage:true});
 await page.getByRole('button',{name:'Abrir menu'}).click();await expect(page.getByRole('dialog')).toBeVisible();await page.keyboard.press('Escape');
});
test('Cadastro de cliente: validação existente impede envio vazio',async({page})=>{
 await prepare(page);let writes=0;page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/customers'))writes++;});
 await page.goto('/clientes/novo');await page.getByRole('button',{name:'Salvar',exact:true}).click();await expect(page.getByText('Nome é obrigatório.')).toBeVisible();expect(writes).toBe(0);
});

for(const width of [1440,390]) test('PDV aberto em '+width+'px',async({page})=>{
 await page.setViewportSize({width,height:900});await prepare(page);
 await page.route('**/api/cash/sessions/open',route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({id:'qa-cash',status:'Open'})}));
 await page.goto('/pdv');await expect(page.getByRole('button',{name:'Finalizar venda'})).toBeVisible();await noOverflow(page);
 await page.screenshot({path:'../docs/orken-experience/screenshots/qa/pdv-'+width+'.png',fullPage:true});
});
test('PDV: falha de consulta não é caixa fechado',async({page})=>{
 await prepare(page);await page.route('**/api/cash/sessions/open',route=>route.fulfill({status:503,body:'{}'}));await page.goto('/pdv');
 await expect(page.getByRole('alert')).toContainText('Não foi possível consultar o caixa');await expect(page.getByText('Caixa não está aberto')).toHaveCount(0);
});
test('Portal público: serviço real da fixture e seleção por teclado',async({page})=>{
 await page.setViewportSize({width:390,height:844});
 await page.route('**/api/public/service/qa**',route=>{
  const path=new URL(route.request().url()).pathname;
  const body=path.endsWith('/catalog')?[{id:'qa-service',name:'Serviço de teste',description:'Registro sintético de revisão',durationMinutes:30,price:50}]:path.endsWith('/professionals')?[{id:'qa-pro',name:'Profissional de teste'}]:{storeName:'Estabelecimento de teste',presetKey:'barbearia',labels:preset.labels,capabilities:preset.capabilities,isBookingEnabled:true,showPrices:true,requiresProfessionalSelection:true,brandColor:'#ffff00'};
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 await page.goto('/agendar/qa');await expect(page.getByRole('button',{name:/Serviço de teste/})).toBeVisible();await noOverflow(page);
 await page.screenshot({path:'../docs/orken-experience/screenshots/qa/portal-390.png',fullPage:true});
 await page.getByRole('button',{name:/Serviço de teste/}).focus();await page.keyboard.press('Enter');await expect(page.getByRole('button',{name:'Profissional de teste',exact:true})).toBeVisible();
});
