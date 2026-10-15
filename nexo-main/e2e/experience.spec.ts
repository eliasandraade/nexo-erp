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
test('Caixa: esperado do backend, recebimentos por forma e histórico de fechamentos',async({page})=>{
 await prepare(page);
 const open={id:'qa-cash',status:'Open',openedByUserId:'qa',openedByName:'Revisão local',closedByUserId:null,closedByName:null,openingBalance:100,closingBalance:null,openedAt:'2026-10-14T08:00:00Z',closedAt:null,notes:null,movements:null,expectedBalance:null,difference:null,closingNotes:null,summary:null};
 const summary={openingBalance:100,cashSales:20,deposits:50,withdrawals:30,refunds:0,expectedBalance:140,receiptsByMethod:[{method:'Cash',type:'Cash',count:1,amount:20},{method:'Pix',type:'Cash',count:1,amount:15}]};
 const closed={...open,id:'qa-old',status:'Closed',closedByName:'Revisão local',closingBalance:85,closedAt:'2026-10-13T20:00:00Z',expectedBalance:90,difference:-5,closingNotes:'Faltou troco'};
 await page.route('**/api/cash/sessions**',route=>{const p=new URL(route.request().url()).pathname;
  const body=p.endsWith('/sessions/open')?open:p.endsWith('/sessions/qa-cash')?{...open,movements:[],summary}:p.endsWith('/sessions')?[open,closed]:{};
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});});
 for(const width of [1440,390]){
  await page.setViewportSize({width,height:900});await page.goto('/caixa');
  await expect(page.getByText('Recebimentos por forma de pagamento')).toBeVisible();
  await expect(page.getByText('Pix',{exact:true})).toBeVisible();
  await expect(page.getByText('Falta R$ 5,00',{exact:false})).toBeVisible();
  await expect(page.getByText('R$ 140,00').first()).toBeVisible();
  await noOverflow(page);
  await page.screenshot({path:'test-results/caixa-'+width+'.png',fullPage:true});
 }
});
test('Venda paga: devolução parcial envia itens, reembolso, motivo e chave de idempotência',async({page})=>{
 await prepare(page);
 const sale={id:'qa-sale',number:42,status:'Paid',customerId:null,customerName:null,soldByUserId:'qa',soldByName:'Revisão local',cashSessionId:'qa-cash',subtotal:100,discountAmount:10,taxAmount:0,total:90,notes:null,confirmedAt:'2026-10-14T10:00:00Z',paidAt:'2026-10-14T10:00:00Z',cancelledAt:null,
  items:[{id:'qa-item',productId:'qa-prod',productName:'Camiseta',productCode:'C1',quantity:2,unitPrice:50,costPrice:20,discountAmount:0,total:100,notes:null}],
  payments:[{id:'p1',method:'Cash',type:'Cash',amount:90,dueDate:null,tenderedAmount:90,changeAmount:0}],createdAt:'2026-10-14T10:00:00Z',updatedAt:'2026-10-14T10:00:00Z',returnedAmount:0,cancellationReason:null};
 let posted: any=null;
 await page.route('**/api/sales/qa-sale**',async route=>{const r=route.request();const p=new URL(r.url()).pathname;
  if(p.endsWith('/returns')&&r.method()==='POST'){posted=r.postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({id:'qa-ret',saleId:'qa-sale',refundMethod:'Pix',refundAmount:45,reason:'Defeito',createdByUserId:'qa',createdAt:'2026-10-14T11:00:00Z',items:[{saleItemId:'qa-item',productId:'qa-prod',quantity:1,amount:45}]})});}
  if(p.endsWith('/returns'))return route.fulfill({status:200,contentType:'application/json',body:'[]'});
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(sale)});});
 await page.goto('/vendas/qa-sale');
 await expect(page.getByRole('button',{name:'Cancelar venda'})).toHaveCount(0);
 await page.getByRole('button',{name:'Registrar devolução'}).click();
 await page.getByLabel('Quantidade devolvida de Camiseta').fill('1');
 await expect(page.getByText('R$ 45,00')).toBeVisible();
 await page.getByLabel('Motivo').fill('Defeito');
 await page.getByRole('button',{name:'Confirmar devolução'}).click();
 await expect.poll(()=>posted).not.toBeNull();
 expect(posted.items).toEqual([{saleItemId:'qa-item',quantity:1}]);
 expect(posted.refundMethod).toBe('Cash');
 expect(posted.reason).toBe('Defeito');
 expect(typeof posted.idempotencyKey).toBe('string');
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
test('Inventário: contagem salva e aplicada; transferência envia destino e quantidades',async({page})=>{
 await prepare(page);
 const count={id:'qa-count',number:3,status:'Open',notes:null,createdAt:'2026-10-14T09:00:00Z',closedAt:null,countedItems:0,
  items:[{productId:'p1',productName:'Arroz 5kg',productCode:'A5',expectedQuantity:10,countedQuantity:null,systemQuantityAtApply:null,difference:null}]};
 let saved: any=null, applied=false, transferBody: any=null;
 await page.route('**/api/stock/counts**',route=>{const r=route.request();const p=new URL(r.url()).pathname;
  if(r.method()==='PUT'){saved=r.postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(count)});}
  if(p.endsWith('/apply')){applied=true;return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({...count,status:'Applied',items:[{...count.items[0],countedQuantity:8,systemQuantityAtApply:10,difference:-2}]})});}
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([count])});});
 await page.goto('/estoque/inventario');
 await page.getByLabel('Quantidade contada de Arroz 5kg').fill('8');
 await page.getByRole('button',{name:'Aplicar ajustes'}).click();
 await expect.poll(()=>applied).toBe(true);
 expect(saved.items).toEqual([{productId:'p1',countedQuantity:8}]);
 await page.setViewportSize({width:390,height:900});await noOverflow(page);await page.setViewportSize({width:1280,height:900});

 await page.route('**/api/stores**',route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'qa-store',name:'Matriz'},{id:'qa-store-2',name:'Filial Centro'}])}));
 await page.route('**/api/stock',route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'s1',productId:'p1',productName:'Arroz 5kg',productCode:'A5',currentQuantity:10,reservedQuantity:0,availableQuantity:10,lastMovementAt:null}])}));
 await page.route('**/api/stock/transfers',route=>{const r=route.request();
  if(r.method()==='POST'){transferBody=r.postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({id:'t1',number:1,fromStoreId:'qa-store',toStoreId:'qa-store-2',direction:'out',notes:null,createdAt:'2026-10-14T10:00:00Z',items:[{productId:'p1',productName:'Arroz 5kg',quantity:4}]})});}
  return route.fulfill({status:200,contentType:'application/json',body:'[]'});});
 await page.goto('/estoque/transferencias');
 await page.getByLabel('Loja de destino').click();await page.getByRole('option',{name:'Filial Centro'}).click();
 await page.getByLabel('Quantidade a transferir de Arroz 5kg').fill('4');
 await page.getByRole('button',{name:/Transferir 1 produto/}).click();
 await expect.poll(()=>transferBody).not.toBeNull();
 expect(transferBody).toMatchObject({toStoreId:'qa-store-2',items:[{productId:'p1',quantity:4}]});
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
test('Compras: criar pedido, adicionar item e receber com vencimento',async({page})=>{
 await prepare(page);
 const base={id:'qa-pur',purchaseNumber:7,status:'Draft',supplierId:'sup1',supplierName:'Distribuidora Teste',totalAmount:0,invoiceNumber:null,receivedAt:null,confirmedAt:null,cancelledAt:null,notes:null,items:[],createdAt:'2026-10-14T09:00:00Z',orderedAt:null,cancellationReason:null};
 let current: any={...base}; let itemBody: any=null, receiveBody: any=null;
 await page.route('http://localhost:5000/api/suppliers**',route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'sup1',name:'Distribuidora Teste',tradeName:null,isActive:true}])}));
 await page.route('http://localhost:5000/api/products**',route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'p1',code:'A5',name:'Arroz 5kg',costPrice:20,salePrice:30,isActive:true}])}));
 await page.route('http://localhost:5000/api/varejo/purchases**',route=>{const r=route.request();const p=new URL(r.url()).pathname;
  const ok=(b:any)=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(b)});
  if(r.method()==='POST'&&p.endsWith('/purchases')){current={...base};return ok(current);}
  if(p.endsWith('/items')){itemBody=r.postDataJSON();current={...current,totalAmount:200,items:[{id:'i1',productId:'p1',productName:'Arroz 5kg',productCode:'A5',quantity:10,unitCost:20,total:200,notes:null}]};return ok(current);}
  if(p.endsWith('/confirm')){receiveBody=r.postDataJSON();current={...current,status:'Confirmed',confirmedAt:'2026-10-14T10:00:00Z'};return ok(current);}
  if(p.endsWith('/qa-pur'))return ok(current);
  return ok([current]);});
 await page.goto('/compras');
 await page.getByLabel('Fornecedor').click();await page.getByRole('option',{name:'Distribuidora Teste'}).click();
 await page.getByRole('button',{name:'Criar pedido'}).click();
 await expect(page).toHaveURL(/\/compras\/qa-pur$/);
 await page.getByLabel('Produto').click();await page.getByRole('option',{name:/Arroz 5kg/}).click();
 await page.getByLabel('Quantidade').fill('10');
 await page.getByRole('button',{name:'Adicionar'}).click();
 await expect.poll(()=>itemBody).not.toBeNull();
 expect(itemBody).toMatchObject({productId:'p1',quantity:10,unitCost:20});
 await page.getByRole('button',{name:'Receber mercadoria'}).click();
 await page.getByLabel('Vencimento da conta').fill('2026-11-14');
 await page.getByRole('button',{name:'Confirmar recebimento'}).click();
 await expect.poll(()=>receiveBody).not.toBeNull();
 expect(receiveBody.paidNow).toBe(false);
 expect(receiveBody.dueDate).toContain('2026-11-14');
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
test('Listas de preço: definir preço e vincular cliente; PDV usa o preço resolvido',async({page})=>{
 await prepare(page);
 const API='http://localhost:5000/api';
 const detail={id:'pl1',name:'Atacado',description:null,isDefault:false,isActive:true,createdAt:'2026-10-14T09:00:00Z',validFrom:null,validUntil:null,priority:0,items:[],customers:[]};
 let priceBody: any=null, linked=false;
 await page.route(`${API}/varejo/price-lists**`,route=>{const r=route.request();const p=new URL(r.url()).pathname;
  const ok=(b:any)=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(b)});
  if(p.endsWith('/products')){priceBody=r.postDataJSON();return ok({...detail,items:[{id:'i1',productId:'p1',productName:'Arroz 5kg',productCode:'A5',price:priceBody.price}]});}
  if(p.includes('/customers/')){linked=true;return ok({...detail,customers:[{customerId:'c1',name:'Mercado Bom'}]});}
  if(p.endsWith('/pl1'))return ok(detail);
  return ok([{...detail,itemCount:0}]);});
 await page.route(`${API}/products**`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'p1',code:'A5',name:'Arroz 5kg',costPrice:null,salePrice:30,isActive:true,trackStock:true}])}));
 await page.route(`${API}/customers**`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'c1',name:'Mercado Bom',isActive:true}])}));
 await page.goto('/listas-de-preco');
 await page.getByRole('button',{name:'Atacado'}).click();
 await page.getByLabel('Produto').click();await page.getByRole('option',{name:/Arroz 5kg/}).click();
 await page.getByLabel('Preço na lista').fill('27,50');
 await page.getByRole('button',{name:'Definir preço'}).click();
 await expect.poll(()=>priceBody).not.toBeNull();
 expect(priceBody).toEqual({productId:'p1',price:27.5});
 await page.getByLabel('Vincular cliente').click();await page.getByRole('option',{name:'Mercado Bom'}).click();
 await page.getByRole('button',{name:'Vincular'}).click();
 await expect.poll(()=>linked).toBe(true);
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
test('Financeiro: livro por competência e caixa, com categorias e lançamentos',async({page})=>{
 await prepare(page);
 const API='http://localhost:5000/api';
 const regimes: string[]=[];
 const entry=(id:string,direction:'In'|'Out',category:string,amount:number,description:string,status='Paid',referenceType='Sale')=>({id,direction,category,amount,description,competenceDate:'2026-10-10T12:00:00Z',dueDate:'2026-10-10T12:00:00Z',paidAt:status==='Paid'?'2026-10-10T12:00:00Z':null,status,storeId:'qa-store',referenceType,referenceId:'r'+id,reversesTransactionId:null});
 await page.route(`${API}/financial/ledger**`,route=>{const regime=new URL(route.request().url()).searchParams.get('regime')!;regimes.push(regime);
  const accrual=regime==='accrual';
  const entries=[entry('e1','In','Revenue',100,'Venda #1 — Dinheiro'),entry('e2','Out','RevenueDeduction',20,'Devolução Venda #1 — Dinheiro','Paid','SaleReturn')].concat(accrual?[entry('e3','In','Revenue',50,'Venda #2 — Outro a prazo','Pending')]:[]);
  const rev=accrual?150:100;
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({regime:accrual?'Accrual':'Cash',from:'2026-10-01T03:00:00Z',to:'2026-11-01T03:00:00Z',totalIn:rev,totalOut:20,net:rev-20,result:rev-20,
   byCategory:[{category:'Revenue',in:rev,out:0,net:rev,count:accrual?2:1},{category:'RevenueDeduction',in:0,out:20,net:-20,count:1}],entries,truncated:false})});});
 await page.goto('/financeiro');
 await expect(page.getByRole('heading',{name:'Financeiro',exact:true})).toBeVisible();
 await expect(page.getByText('Venda #2 — Outro a prazo')).toBeVisible();
 await expect(page.getByText('Devoluções e estornos').first()).toBeVisible();
 await expect(page.getByText('Em aberto',{exact:true})).toBeVisible();
 await page.getByRole('tab',{name:'Caixa'}).click();
 await expect.poll(()=>regimes.includes('cash')).toBe(true);
 await expect(page.getByText('Venda #2 — Outro a prazo')).toHaveCount(0);
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
test('Contas a receber: aging, baixa parcial com juros e novo parcelamento',async({page})=>{
 await prepare(page);
 const API='http://localhost:5000/api';
 let settleBody: any=null, installmentsBody: any=null;
 const title={id:'t1',description:'Venda #12 — Outro a prazo',customerId:'c1',customerName:'Mercado Bom',amount:100,dueDate:'2026-09-01T12:00:00Z',competenceDate:'2026-08-01T12:00:00Z',paidAt:null,status:'Overdue',daysOverdue:43,category:'Revenue',storeId:'qa-store',referenceType:'Sale',referenceId:'s12',parentTransactionId:null};
 await page.route(`${API}/financial/receivables**`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({items:[title],aging:{notDue:50,overdue1To30:0,overdue31To60:100,overdueOver60:0,totalOpen:150,openCount:2,overdueCount:1},truncated:false})}));
 await page.route(`${API}/financial/transactions/t1/settle`,route=>{settleBody=route.request().postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:'{}'});});
 await page.route(`${API}/financial/transactions/installments`,route=>{installmentsBody=route.request().postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:'[]'});});
 await page.goto('/financeiro/receber');
 await expect(page.getByRole('heading',{name:'Contas a receber',exact:true})).toBeVisible();
 await expect(page.getByText('Mercado Bom')).toBeVisible();
 await expect(page.getByText('há 43 dias')).toBeVisible();
 await page.getByRole('button',{name:'Receber',exact:true}).click();
 await page.getByLabel('Valor recebido').fill('60,00');
 await page.getByLabel('Juros').fill('2,50');
 await expect(page.getByText('Fica em aberto: R$ 40,00',{exact:false})).toBeVisible();
 await page.getByRole('button',{name:'Confirmar recebimento'}).click();
 await expect.poll(()=>settleBody).not.toBeNull();
 expect(settleBody).toMatchObject({amount:60,interest:2.5,fine:0,discount:0});
 await page.getByRole('button',{name:'Novo recebimento'}).click();
 await page.getByLabel('Descrição').fill('Instalação');
 await page.getByLabel('Valor total').fill('1.000,00');
 await page.getByLabel('Parcelas').fill('3');
 await page.getByRole('button',{name:'Lançar'}).click();
 await expect.poll(()=>installmentsBody).not.toBeNull();
 expect(installmentsBody).toMatchObject({transactionType:'Receivable',totalAmount:1000,installments:3,description:'Instalação',customerId:null});
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
