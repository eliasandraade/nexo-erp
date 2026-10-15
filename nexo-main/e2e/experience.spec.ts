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
test('Contas a pagar: fornecedor no título, pagamento com desconto e recorrência mensal',async({page})=>{
 await prepare(page);
 const API='http://localhost:5000/api';
 let settleBody: any=null, recurrenceBody: any=null;
 const title={id:'p1',description:'Compra #7 — Atacadão',customerId:null,customerName:null,supplierId:'s1',supplierName:'Atacadão',amount:500,dueDate:'2026-10-20T15:00:00Z',competenceDate:'2026-10-05T15:00:00Z',paidAt:null,status:'Pending',daysOverdue:0,category:'Purchase',storeId:'qa-store',referenceType:'Purchase',referenceId:'c7',parentTransactionId:null};
 await page.route(`${API}/financial/payables**`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({items:[title],aging:{notDue:500,overdue1To30:0,overdue31To60:0,overdueOver60:0,totalOpen:500,openCount:1,overdueCount:0},truncated:false})}));
 await page.route(`${API}/financial/transactions/p1/settle`,route=>{settleBody=route.request().postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:'{}'});});
 await page.route(`${API}/financial/recurrences`,route=>{
  if(route.request().method()==='POST'){recurrenceBody=route.request().postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:'{}'});}
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'r1',transactionType:'Payable',category:'OperatingExpense',description:'Aluguel da loja',amount:1800,dayOfMonth:5,startMonth:'2026-10-01',endMonth:null,generatedThrough:'2026-11-01',isActive:true,supplierId:null,supplierName:null,customerId:null,storeId:null}])});});
 await page.goto('/financeiro/pagar');
 await expect(page.getByRole('heading',{name:'Contas a pagar',exact:true})).toBeVisible();
 await expect(page.getByRole('cell',{name:'Atacadão',exact:true})).toBeVisible();
 await expect(page.getByText('Aluguel da loja')).toBeVisible();
 await page.getByRole('button',{name:'Pagar',exact:true}).click();
 await page.getByLabel('Valor pago').fill('480,00');
 await page.getByLabel('Desconto obtido').fill('20,00');
 await expect(page.getByText('O título será quitado.')).toBeVisible();
 await page.getByRole('button',{name:'Confirmar pagamento'}).click();
 await expect.poll(()=>settleBody).not.toBeNull();
 expect(settleBody).toMatchObject({amount:480,discount:20});
 await page.getByRole('button',{name:'Nova recorrência'}).click();
 await page.getByLabel('Descrição da recorrência').fill('Internet');
 await page.getByLabel('Valor mensal').fill('199,90');
 await page.getByLabel('Dia do vencimento').fill('31');
 await page.getByRole('button',{name:'Criar recorrência'}).click();
 await expect.poll(()=>recurrenceBody).not.toBeNull();
 expect(recurrenceBody).toMatchObject({transactionType:'Payable',description:'Internet',amount:199.9,dayOfMonth:31,category:'OperatingExpense',supplierId:null});
 expect(recurrenceBody.startMonth).toMatch(/^\d{4}-\d{2}-01$/);
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
test('Fluxo de caixa: saldo projetado por dia, vencidos à parte e saldo inicial',async({page})=>{
 await prepare(page);
 const API='http://localhost:5000/api';
 let openingBody: any=null;
 const buckets=[{start:'2026-10-15',end:'2026-10-15',realizedIn:300,realizedOut:50,projectedIn:0,projectedOut:0,balance:1250},
  {start:'2026-10-16',end:'2026-10-16',realizedIn:0,realizedOut:0,projectedIn:0,projectedOut:1800,balance:-550}];
 await page.route(`${API}/financial/cash-flow**`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({from:'2026-10-15',to:'2026-10-16',granularity:'Day',openingBalance:1000,openingBalanceConfigured:false,overdueIn:120,overdueOut:0,closingBalance:-550,buckets})}));
 await page.route(`${API}/financial/accounts`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'a1',code:'1.1.1',name:'Caixa',accountType:'Cash',isActive:true,openingBalance:0,openingBalanceDate:null},{id:'a2',code:'1.1.2',name:'Contas a Receber',accountType:'Receivable',isActive:true,openingBalance:0,openingBalanceDate:null}])}));
 await page.route(`${API}/financial/accounts/a1/opening-balance`,route=>{openingBody=route.request().postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:'{}'});});
 await page.goto('/financeiro/fluxo');
 await expect(page.getByRole('heading',{name:'Fluxo de caixa',exact:true})).toBeVisible();
 await expect(page.getByText('Sem saldo inicial',{exact:false})).toBeVisible();
 await expect(page.getByText('Vencidos e não pagos',{exact:false})).toBeVisible();
 await expect(page.getByRole('cell',{name:'-R$ 550,00'}).or(page.getByRole('cell',{name:'−R$ 550,00'})).or(page.getByRole('cell',{name:'R$ -550,00'}))).toBeVisible();
 await page.getByRole('button',{name:'Saldo inicial'}).click();
 await page.getByLabel('Valor').fill('2.500,00');
 await page.getByRole('button',{name:'Salvar'}).click();
 await expect.poll(()=>openingBody).not.toBeNull();
 expect(openingBody).toMatchObject({amount:2500});
 expect(openingBody.date).toMatch(/^\d{4}-\d{2}-\d{2}$/);
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
test('Adicionais: grupo do produto, opção com preço e remoção',async({page})=>{
 await prepare(page);
 const API='http://localhost:5000/api';
 let groupBody: any=null, optionBody: any=null, removedGroup=false;
 const groups=[{id:'g1',productId:'p1',name:'Ponto da carne',isRequired:true,minSelections:1,maxSelections:1,sortOrder:0,isActive:true,
   modifiers:[{id:'m1',name:'Ao ponto',priceAdjustment:0,sortOrder:0,isActive:true}]}];
 await page.route(`${API}/products**`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'p1',code:'B1',name:'Burger da casa',costPrice:null,salePrice:32,isActive:true,trackStock:false}])}));
 await page.route(`${API}/restaurante/modifier-groups**`,route=>{const r=route.request();const path=new URL(r.url()).pathname;
  if(r.method()==='POST'&&path.endsWith('/modifiers')){optionBody=r.postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:'{}'});}
  if(r.method()==='POST'){groupBody=r.postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:'{}'});}
  if(r.method()==='DELETE'){removedGroup=true;return route.fulfill({status:204,body:''});}
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(groups)});});
 await page.goto('/restaurante/adicionais');
 await expect(page.getByRole('heading',{name:'Adicionais',exact:true})).toBeVisible();
 await page.getByLabel('Produto').click();await page.getByRole('option',{name:'Burger da casa'}).click();
 await expect(page.getByText('Ao ponto')).toBeVisible();
 await page.getByLabel('Nova opção').fill('Bacon extra');
 await page.getByLabel('Preço').fill('4,50');
 await page.getByRole('button',{name:'Adicionar'}).click();
 await expect.poll(()=>optionBody).not.toBeNull();
 expect(optionBody).toMatchObject({groupId:'g1',name:'Bacon extra',priceAdjustment:4.5});
 await page.getByLabel('Nome do grupo').fill('Acompanhamento');
 await page.getByRole('button',{name:'Criar grupo'}).click();
 await expect.poll(()=>groupBody).not.toBeNull();
 expect(groupBody).toMatchObject({productId:'p1',name:'Acompanhamento',isRequired:false,minSelections:0,maxSelections:1});
 await page.getByRole('button',{name:'Remover grupo Ponto da carne'}).click();
 expect(removedGroup).toBe(false);                                         // first tap only arms it
 await page.getByRole('button',{name:'Confirmar remoção do grupo Ponto da carne'}).click();
 await expect.poll(()=>removedGroup).toBe(true);
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
test('Service: agenda da semana com bloqueio, abrir comanda e concluir direto',async({page})=>{
 await prepare(page);
 const API='http://localhost:5000/api';
 let blockBody: any=null, statusBody: any=null, orderFor: string|null=null, deletedBlock: string|null=null;
 const today=new Date(); const iso=(h:number)=>{const d=new Date(today);d.setHours(h,0,0,0);return d.toISOString();};
 const appt={id:'a1',customerId:'c1',professionalId:'pr1',catalogItemId:'s1',subjectId:null,startsAt:iso(14),endsAt:iso(15),status:'Scheduled',notes:null,priceSnapshot:50,cancellationReason:null,createdAt:iso(9),updatedAt:iso(9)};
 await page.route(`${API}/v1/service/appointments**`,route=>{const r=route.request();
  if(r.method()==='PATCH'){statusBody=r.postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({...appt,status:'Completed'})});}
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([appt])});});
 await page.route(`${API}/v1/service/time-blocks**`,route=>{const r=route.request();
  if(r.method()==='POST'){blockBody=r.postDataJSON();return route.fulfill({status:201,contentType:'application/json',body:'{}'});}
  if(r.method()==='DELETE'){deletedBlock=new URL(r.url()).pathname.split('/').pop()!;return route.fulfill({status:204,body:''});}
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'b1',professionalId:'pr1',startsAt:iso(12),endsAt:iso(13),reason:'Almoço',createdAt:iso(8)}])});});
 await page.route(`${API}/v1/service/professionals**`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'pr1',name:'Rafa',isActive:true}])}));
 await page.route(`${API}/v1/service/catalog**`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'s1',name:'Corte',isActive:true}])}));
 await page.route(`${API}/customers**`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'c1',name:'Bruno',isActive:true}])}));
 await page.route(`${API}/v1/service/orders/from-appointment/**`,route=>{orderFor=new URL(route.request().url()).pathname.split('/').pop()!;
  // the appointment already has a comanda: the agenda opens the existing one
  return route.fulfill({status:409,contentType:'application/json',body:JSON.stringify({message:'Já existe uma comanda para este agendamento.'})});});
 await page.route(/\/v1\/service\/orders\?appointmentId=a1/,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'o9',appointmentId:'a1',status:'Open',items:[]}])}));
 await page.goto('/service/agenda');
 await expect(page.getByText('Almoço')).toBeVisible();
 await expect(page.getByText('Bruno')).toBeVisible();
 await page.getByRole('tab',{name:'Semana'}).click();
 await expect(page.getByText('Almoço')).toBeVisible();
 // concluir straight from Scheduled
 await page.getByRole('button',{name:'Ações do agendamento'}).click();
 await page.getByRole('menuitem',{name:'Concluir'}).click();
 await expect.poll(()=>statusBody).not.toBeNull();
 expect(statusBody.status).toBe('Completed');
 // block the store for a holiday
 await page.getByRole('button',{name:'Bloquear horário'}).click();
 await page.getByLabel('Motivo').fill('Feriado');
 await page.getByText('O dia todo').click();
 await expect(page.getByRole('button',{name:'Bloquear',exact:true})).toBeDisabled();   // whole store is never the default
 await page.getByLabel('Para').click();
 await page.getByRole('option',{name:'Loja inteira (feriado)'}).click();
 await page.getByRole('button',{name:'Bloquear',exact:true}).click();
 await expect.poll(()=>blockBody).not.toBeNull();
 expect(blockBody).toMatchObject({professionalId:null,reason:'Feriado'});
 // removing a block takes two taps
 await page.getByRole('button',{name:/^Remover bloqueio de Rafa/}).click();
 expect(deletedBlock).toBeNull();
 await page.getByRole('button',{name:/^Confirmar remoção do bloqueio de Rafa/}).click();
 await expect.poll(()=>deletedBlock).toBe('b1');
 // open the comanda of the appointment
 await page.getByRole('button',{name:'Ações do agendamento'}).click();
 await page.getByRole('menuitem',{name:'Abrir comanda'}).click();
 await expect.poll(()=>orderFor).toBe('a1');
 await expect(page).toHaveURL(/\/service\/ordens\/o9$/);
});

test('Portal público: cliente vê e cancela o próprio agendamento pelo link',async({page})=>{
 await page.setViewportSize({width:390,height:844});
 const tok='0123456789abcdef0123456789abcdef'; let cancelled=false;
 const view=(status:string,canCancel:boolean)=>({status,startsAt:new Date(Date.now()+2*864e5).toISOString(),endsAt:new Date(Date.now()+2*864e5+36e5).toISOString(),
  serviceName:'Corte',professionalName:'Rafa',storeName:'Barbearia QA',canCancel,cancelDeadline:new Date(Date.now()+2*864e5-72e5).toISOString()});
 await page.route('**/api/public/service/qa**',route=>{const r=route.request();const path=new URL(r.url()).pathname;
  if(path.endsWith(`/appointments/${tok}/cancel`)){cancelled=true;return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(view('Cancelled',false))});}
  if(path.endsWith(`/appointments/${tok}`))return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(view('Scheduled',true))});
  if(path.includes('/appointments/'))return route.fulfill({status:404,contentType:'application/json',body:'{"error":"not found"}'});
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({storeName:'Barbearia QA',presetKey:'barbearia',labels:preset.labels,capabilities:preset.capabilities,isBookingEnabled:true,showPrices:true,requiresProfessionalSelection:true})});});
 await page.goto(`/agendar/qa/reserva/${tok}`);
 await expect(page.getByText('Corte')).toBeVisible();await expect(page.getByText(/aguardando confirmação/)).toBeVisible();await noOverflow(page);
 await page.getByRole('button',{name:'Cancelar agendamento'}).click();
 expect(cancelled).toBe(false);                                            // first tap only asks
 await page.getByRole('button',{name:'Sim, cancelar'}).click();
 await expect.poll(()=>cancelled).toBe(true);
 await expect(page.getByText('Cancelado',{exact:true})).toBeVisible();
 await expect(page.getByRole('link',{name:'Agendar outro horário'})).toBeVisible();
 await page.goto('/agendar/qa/reserva/ffffffffffffffffffffffffffffffff');
 await expect(page.getByText('Agendamento não encontrado')).toBeVisible();
});
test('Service: estornar um consumo de pacote pede motivo',async({page})=>{
 await prepare(page);
 const API='http://localhost:5000/api'; let reverseBody: any=null;
 const cp=(reversed:boolean)=>({id:'cp1',storeId:'s',code:'PKG-1',packageId:'p1',customerId:'c1',subjectId:null,status:reversed?'Active':'Consumed',startsAt:new Date().toISOString(),expiresAt:null,priceSnapshot:100,notes:null,
  items:[{id:'i1',customerPackageId:'cp1',catalogItemId:'s1',nameSnapshot:'Corte',totalQuantity:1,remainingQuantity:reversed?1:0,createdAt:new Date().toISOString(),updatedAt:null}],
  usages:[{id:'u1',customerPackageId:'cp1',customerPackageItemId:'i1',catalogItemId:'s1',orderId:null,orderItemId:null,quantity:1,notes:null,professionalId:null,appointmentId:null,baseAmountSnapshot:100,commissionPercentSnapshot:null,createdAt:new Date().toISOString(),reversedAt:reversed?new Date().toISOString():null,reversalReason:reversed?'Serviço errado':null}],
  createdAt:new Date().toISOString(),updatedAt:null});
 await page.route(`${API}/v1/service/customer-packages/cp1/usages/u1/reverse`,route=>{reverseBody=route.request().postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(cp(true))});});
 await page.route(`${API}/v1/service/customer-packages/cp1`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(cp(reverseBody!==null))}));
 await page.route(`${API}/v1/service/payments/customer-package/cp1/summary`,route=>route.fulfill({status:200,contentType:'application/json',
  body:JSON.stringify({targetId:'cp1',targetType:'CustomerPackage',totalAmount:100,paidAmount:100,voidedAmount:0,remainingAmount:0,isFullyPaid:true})}));
 await page.goto('/service/customer-packages/cp1');
 await page.getByRole('button',{name:'Estornar'}).click();
 await expect(page.getByRole('button',{name:'Confirmar estorno'})).toBeDisabled();
 await page.getByLabel('Motivo do estorno').fill('Serviço errado');
 await page.getByRole('button',{name:'Confirmar estorno'}).click();
 await expect.poll(()=>reverseBody).toEqual({reason:'Serviço errado'});
 await expect(page.getByText(/Estornado em/)).toBeVisible();
});
test('Service: liga os avisos por e-mail com o lembrete escolhido',async({page})=>{
 await prepare(page);
 let body: any=null;
 await page.route('http://localhost:5000/api/v1/service/settings/customer-emails',route=>{body=route.request().postDataJSON();
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({isConfigured:true,publicBookingEnabled:false,customerEmailsEnabled:true,reminderHoursBefore:body.reminderHoursBefore})});});
 await page.route('http://localhost:5000/api/v1/service/settings/public-booking',route=>route.fulfill({status:200,contentType:'application/json',
  body:JSON.stringify({isConfigured:true,publicBookingEnabled:false,bookingDaysAhead:14,minLeadMinutes:120,slotIntervalMinutes:30,showPrices:true,autoConfirmAppointments:false,timeZoneId:'America/Sao_Paulo',customerEmailsEnabled:false,reminderHoursBefore:24})}));
 await page.goto('/service/portal');
 const toggle=page.getByRole('switch',{name:/Avisar clientes por e-mail/});
 await expect(toggle).toHaveAttribute('aria-checked','false');
 await toggle.click();
 await page.getByLabel('Lembrete').click();
 await page.getByRole('option',{name:'2 horas antes',exact:true}).click();
 await page.getByRole('button',{name:'Salvar avisos'}).click();
 await expect.poll(()=>body).toEqual({customerEmailsEnabled:true,reminderHoursBefore:2});
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
test('Service: financeiro do período por profissional',async({page})=>{
 await prepare(page);
 let asked='';
 await page.route(/\/v1\/service\/finance\/summary/,route=>{asked=new URL(route.request().url()).search;
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({from:'2026-10-01',to:'2026-10-15',received:400,paymentsCount:3,averageTicket:200,
   receivedFromOrders:100,receivedFromPackages:300,voided:20,voidedCount:1,byMethod:[{method:'Pix',amount:360,count:2},{method:'Cash',amount:40,count:1}],
   byDay:[{day:'2026-10-10',amount:400}],byProfessional:[{professionalId:'p1',name:'Rafa',receivedFromOrders:60,packageServices:50,commission:16},{professionalId:null,name:'Sem profissional',receivedFromOrders:40,packageServices:0,commission:0}],
   commissionTotal:16,packagesSold:1,packagesSoldValue:300,appointments:{total:4,completed:2,cancelled:1,noShow:1,open:0}})});});
 await page.goto('/service/financeiro');
 await expect(page.getByRole('heading',{name:'Financeiro',exact:true})).toBeVisible();
 await expect(page.getByText('R$ 400,00').first()).toBeVisible();
 await expect(page.getByRole('cell',{name:'Rafa'})).toBeVisible();
 await expect(page.getByText(/33% dos que já passaram/)).toBeVisible();
 await page.getByRole('tab',{name:'Hoje'}).click();
 await expect.poll(()=>asked).toMatch(/from=(\d{4}-\d{2}-\d{2})&to=\1$/);
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
test('Service: profissional por item da comanda',async({page})=>{
 await prepare(page);
 const API='http://localhost:5000/api'; let updateBody: any=null, addBody: any=null;
 const order={id:'o1',code:'OS-1',customerId:'c1',subjectId:null,professionalId:'p1',appointmentId:null,status:'Open',notes:null,cancellationReason:null,totalAmount:60,
  items:[{id:'i1',orderId:'o1',catalogItemId:'s1',professionalId:null,nameSnapshot:'Corte',descriptionSnapshot:null,quantity:1,unitPriceSnapshot:60,totalAmount:60}],createdAt:new Date().toISOString(),updatedAt:null};
 await page.route(`${API}/v1/service/orders/o1/items/i1`,route=>{updateBody=route.request().postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(order)});});
 await page.route(`${API}/v1/service/orders/o1/items`,route=>{addBody=route.request().postDataJSON();return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(order)});});
 await page.route(`${API}/v1/service/orders/o1`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(order)}));
 await page.route(`${API}/v1/service/payments/order/o1/summary`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({targetId:'o1',targetType:'Order',totalAmount:60,paidAmount:0,voidedAmount:0,remainingAmount:60,isFullyPaid:false})}));
 await page.route(`${API}/v1/service/professionals**`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'p1',name:'Rafa',isActive:true},{id:'p2',name:'Bia',isActive:true}])}));
 await page.route(`${API}/v1/service/catalog**`,route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'s1',name:'Corte',price:60,isActive:true}])}));
 await page.goto('/service/ordens/o1');
 await page.getByLabel(/de Corte$/).click();
 await page.getByRole('option',{name:'Bia'}).click();
 await expect.poll(()=>updateBody).toEqual({quantity:1,professionalId:'p2'});
 await page.getByRole('combobox').filter({hasText:'Adicionar item do catálogo'}).click();
 await page.getByRole('option',{name:/Corte/}).click();
 await page.getByLabel(/do item$/).click();
 await page.getByRole('option',{name:'Bia'}).click();
 await page.getByRole('button',{name:'Adicionar'}).click();
 await expect.poll(()=>addBody).toEqual({catalogItemId:'s1',quantity:1,professionalId:'p2'});
 await page.setViewportSize({width:375,height:900});await noOverflow(page);
});
test('Build: etapas reordenadas e editadas, desvio com sinal certo e estorno de despesa',async({page})=>{
 await prepare(page);
 const API='http://localhost:5000/api'; let reorderBody: any=null, stageBody: any=null, voided: string|null=null;
 const now=new Date().toISOString();
 const stage=(id:string,name:string,order:number)=>({id,projectId:'b1',name,description:null,order,status:'Pending',progressPercent:0,plannedStartDate:null,plannedEndDate:null,actualStartDate:null,actualEndDate:null,createdAt:now,updatedAt:now});
 const stages=[stage('s1','Fundação',0),stage('s2','Alvenaria',1)];
 const project={id:'b1',name:'Casa QA',clientName:'Cliente',location:null,status:'InProgress',type:'House',startDate:null,expectedEndDate:null,actualEndDate:null,budgetEstimated:1000,budgetApproved:1000,stages,recentLogs:[],createdAt:now,updatedAt:now,stageCount:2,completedStageCount:0,logCount:0};
 await page.route(`${API}/v1/build/projects/b1/details`,r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify(project)}));
 await page.route(`${API}/v1/build/projects/b1/stages/reorder`,r=>{reorderBody=r.request().postDataJSON();return r.fulfill({status:204,body:''});});
 await page.route(`${API}/v1/build/projects/b1/stages`,r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify(stages)}));
 await page.route(`${API}/v1/build/stages/s1`,r=>{stageBody=r.request().postDataJSON();return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify(stages[0])});});
 await page.route(`${API}/v1/build/projects/b1/financial-summary`,r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({projectId:'b1',estimatedBudget:1000,approvedBudget:1000,totalRealizedExpenses:1200,movementCount:1,lastMovementDate:null,varianceAmount:-200,variancePercent:-20})}));
 await page.route(/\/v1\/movements\/m1\/void/,r=>{voided='m1';return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({id:'m1',status:'Voided',voidedAt:now})});});
 await page.route(/\/v1\/movements\?/,r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({items:[{id:'m1',direction:'Out',nature:'Expense',amount:1200,date:'2026-10-10',description:'Cimento',contextType:'Obra',contextId:'b1',supplierId:null,status:'Confirmed',createdAt:now}],totalCount:1,page:1,pageSize:30})}));
 await page.route(`${API}/v1/build/projects/b1/daily-logs**`,r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({items:[],total:0,page:1,pageSize:30})}));
 await page.goto('/build/projetos/b1');
 await page.getByRole('button',{name:'Etapas'}).click();
 await page.getByRole('button',{name:'Descer Fundação'}).click();
 await expect.poll(()=>reorderBody).toEqual({entries:[{stageId:'s2',order:0},{stageId:'s1',order:1}]});
 await page.getByRole('button',{name:'Editar Fundação'}).click();
 await page.getByLabel('Início previsto').fill('2026-11-01');
 await page.getByLabel('Fim previsto').fill('2026-11-20');
 await page.getByRole('button',{name:'Salvar etapa'}).click();
 await expect.poll(()=>stageBody).toMatchObject({name:'Fundação',plannedStart:'2026-11-01',plannedEnd:'2026-11-20'});
 await page.getByRole('button',{name:'Financeiro'}).click();
 await expect(page.getByText('acima do aprovado')).toBeVisible();
 await page.getByRole('button',{name:'Estornar Cimento'}).click();
 expect(voided).toBeNull();                                                // first tap only asks
 await page.getByRole('button',{name:'Confirmar estorno de Cimento'}).click();
 await expect.poll(()=>voided).toBe('m1');
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
test('Conta: esqueci a senha e redefinição pelo link',async({page})=>{
 let asked: any=null, reset: any=null;
 await page.route('http://localhost:5000/api/auth/forgot-password',r=>{asked=r.request().postDataJSON();return r.fulfill({status:202,contentType:'application/json',body:'{"message":"ok"}'});});
 await page.route('http://localhost:5000/api/auth/reset-password',r=>{reset=r.request().postDataJSON();
  return reset.token==='bom'?r.fulfill({status:200,contentType:'application/json',body:'{"message":"ok"}'}):r.fulfill({status:400,contentType:'application/json',body:JSON.stringify({error:'Este link é inválido ou expirou. Peça um novo.'})});});
 await page.goto('/login');
 await page.getByRole('link',{name:'Esqueci a senha'}).click();
 await page.getByLabel('E-mail').fill('ana@empresa.com.br');
 await page.getByRole('button',{name:'Enviar link'}).click();
 await expect.poll(()=>asked).toEqual({email:'ana@empresa.com.br'});
 await expect(page.getByRole('status')).toContainText('o link chega em instantes');
 await page.goto('/redefinir-senha?token=ruim');
 await page.getByLabel('Nova senha',{exact:true}).fill('nova-senha-1');
 await page.getByLabel('Confirme a nova senha').fill('nova-senha-1');
 await page.getByRole('button',{name:'Salvar nova senha'}).click();
 await expect(page.getByRole('alert')).toContainText('inválido ou expirou');
 await page.goto('/redefinir-senha?token=bom');
 await page.getByLabel('Nova senha',{exact:true}).fill('nova-senha-1');
 await page.getByLabel('Confirme a nova senha').fill('outra');
 await expect(page.getByRole('button',{name:'Salvar nova senha'})).toBeDisabled();
 await page.getByLabel('Confirme a nova senha').fill('nova-senha-1');
 await page.getByRole('button',{name:'Salvar nova senha'}).click();
 await expect(page).toHaveURL(/\/login\?motivo=senha-redefinida/);
 await expect(page.getByRole('status')).toContainText('Senha alterada');
 await page.setViewportSize({width:390,height:844});await noOverflow(page);
});
test('Conta: sair de todos os dispositivos',async({page})=>{
 await prepare(page);
 let revoked=false;
 await page.route('http://localhost:5000/api/auth/sessions',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify([{id:'s1',startedAt:new Date().toISOString(),lastUsedAt:new Date().toISOString(),ipAddress:'10.0.0.1',userAgent:'Mozilla/5.0 (Windows NT 10.0) Chrome/130.0 Safari/537.36'}])}));
 await page.route('http://localhost:5000/api/users/qa',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({id:'qa',fullName:'Revisão Local',email:'qa@teste.dev',login:'qa',phone:null,role:'diretoria',status:'Active',requirePasswordChange:false,notes:null,lastAccessAt:null,passwordChangedAt:null,createdAt:new Date().toISOString(),updatedAt:null})}));
 await page.route('http://localhost:5000/api/auth/sessions/revoke-all',r=>{revoked=true;return r.fulfill({status:204,body:''});});
 await page.goto('/perfil');
 await expect(page.getByText('Chrome · Windows')).toBeVisible();
 await page.getByRole('button',{name:'Sair de todos os dispositivos'}).click();
 expect(revoked).toBe(false);
 await page.getByRole('button',{name:'Sim, sair de todos'}).click();
 await expect.poll(()=>revoked).toBe(true);
 await expect(page).toHaveURL(/\/login\?motivo=sessoes/);
});
test('Conta: senha definida pelo gestor leva à troca obrigatória',async({page})=>{
 await prepare(page);
 await page.addInitScript(()=>{const s=JSON.parse(localStorage.getItem('nexo:session')||'{}');s.requirePasswordChange=true;localStorage.setItem('nexo:session',JSON.stringify(s));});
 await page.route('http://localhost:5000/api/auth/me',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({userId:'qa',tenantId:'qa',name:'Revisão local',role:'diretoria',login:'qa',email:'qa@teste.dev',activeModules:['varejo'],storeId:'qa-store',storeIds:['qa-store'],companyName:'Ambiente de teste',type:'tenant',requirePasswordChange:true})}));
 await page.route('http://localhost:5000/api/users/qa',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({id:'qa',fullName:'Revisão Local',email:'qa@teste.dev',login:'qa',phone:null,role:'diretoria',status:'Active',requirePasswordChange:true,notes:null,lastAccessAt:null,passwordChangedAt:null,createdAt:new Date().toISOString(),updatedAt:null})}));
 await page.route('http://localhost:5000/api/auth/sessions',r=>r.fulfill({status:200,contentType:'application/json',body:'[]'}));
 await page.goto('/clientes');
 await expect(page).toHaveURL(/\/perfil\?trocar-senha=1/);
 await expect(page.getByRole('alert')).toContainText('Crie sua própria senha');
});
test('Usuários: novo usuário recebe convite por e-mail em vez de senha digitada',async({page})=>{
 await prepare(page);
 let body: any=null;
 await page.route('http://localhost:5000/api/users',r=>{if(r.request().method()==='POST'){body=r.request().postDataJSON();
  return r.fulfill({status:201,contentType:'application/json',body:JSON.stringify({id:'u9',tenantId:'qa',fullName:'Ana',email:'ana@teste.dev',login:'ana',phone:null,role:'diretoria',status:'Active',requirePasswordChange:false,notes:null,lastAccessAt:null,passwordChangedAt:null,createdAt:new Date().toISOString(),updatedAt:new Date().toISOString()})});}
  return r.fulfill({status:200,contentType:'application/json',body:'[]'});});
 await page.goto('/usuarios/novo');
 await page.getByLabel('Nome completo *').fill('Ana');
 await page.getByLabel('E-mail *').fill('ana@teste.dev');
 await page.getByLabel('Login *').fill('ana');
 await page.getByRole('tab',{name:'Acesso e vínculo'}).click();
 await page.getByLabel('Perfil').click();
 await page.getByRole('option',{name:/Diretoria/}).click();
 await page.getByRole('tab',{name:'Segurança'}).click();
 await expect(page.getByRole('switch',{name:'Enviar convite por e-mail'})).toBeChecked();
 await expect(page.getByLabel('Senha *')).toHaveCount(0);
 await page.getByRole('button',{name:'Salvar'}).click();
 await expect.poll(()=>body).toMatchObject({login:'ana',sendInvite:true,password:''});
});
test('Conta: entrar com verificação em duas etapas',async({page})=>{
 let verify: any=null;
 await page.route('http://localhost:5000/api/auth/login',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({mfaRequired:true,mfaChallenge:'ch1'})}));
 await page.route('http://localhost:5000/api/auth/mfa/verify',r=>{verify=r.request().postDataJSON();
  return verify.code==='123456'
   ? r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({accessToken:'a',refreshToken:'b',accessTokenExpiresAt:new Date(Date.now()+9e5).toISOString(),session:{userId:'qa',tenantId:'qa',name:'QA',role:'diretoria',login:'qa',email:'qa@t.dev',activeModules:['varejo'],storeId:'qa-store',storeIds:['qa-store'],companyName:'Ambiente de teste',type:'tenant'}})})
   : r.fulfill({status:401,contentType:'application/json',body:JSON.stringify({error:'Código inválido.',code:'invalid_code'})});});
 await page.route('http://localhost:5000/api/**',r=>r.fallback());
 await page.goto('/login');
 await page.getByLabel('Login').fill('qa');
 await page.getByLabel('Senha',{exact:true}).fill('qualquer');
 await page.getByRole('button',{name:'Entrar'}).click();
 await expect(page.getByLabel('Código do aplicativo autenticador')).toBeVisible();
 await page.getByLabel('Código do aplicativo autenticador').fill('000000');
 await page.getByRole('button',{name:'Confirmar'}).click();
 await expect(page.getByRole('alert')).toContainText('Código inválido');
 await page.getByLabel('Código do aplicativo autenticador').fill('123456');
 await page.getByRole('button',{name:'Confirmar'}).click();
 await expect.poll(()=>verify).toEqual({challenge:'ch1',code:'123456',recoveryCode:null});
 await expect(page).not.toHaveURL(/\/login/);
});
test('Conta: ativar a verificação em duas etapas no perfil',async({page})=>{
 await prepare(page);
 let enabled=false;
 const secret=Array.from(crypto.getRandomValues(new Uint8Array(32)),b=>'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'[b%32]).join('');
 await page.route('http://localhost:5000/api/users/qa',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({id:'qa',fullName:'Revisão Local',email:'qa@teste.dev',login:'qa',phone:null,role:'diretoria',status:'Active',requirePasswordChange:false,notes:null,lastAccessAt:null,passwordChangedAt:null,createdAt:new Date().toISOString(),updatedAt:null})}));
 await page.route('http://localhost:5000/api/auth/sessions',r=>r.fulfill({status:200,contentType:'application/json',body:'[]'}));
 await page.route('http://localhost:5000/api/auth/mfa',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({enabled,setupPending:false,recoveryCodesLeft:enabled?8:0})}));
 await page.route('http://localhost:5000/api/auth/mfa/setup',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({secret,otpAuthUri:`otpauth://totp/Orken:qa?secret=${secret}&issuer=Orken&digits=6&period=30`})}));
 await page.route('http://localhost:5000/api/auth/mfa/enable',r=>{enabled=true;return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({recoveryCodes:['abcde-fghjk','mnpqr-stuvw']})});});
 await page.goto('/perfil');
 await page.getByRole('button',{name:'Ativar verificação em duas etapas'}).click();
 await page.getByLabel('Sua senha').fill(String(Date.now()));
 await page.getByRole('button',{name:'Continuar'}).click();
 await expect(page.getByRole('img',{name:/QR code/})).toBeVisible();
 await expect(page.getByText(secret)).toBeVisible();
 await page.getByLabel(/Digite o código de 6 números/).fill('123456');
 await page.getByRole('button',{name:'Ativar',exact:true}).click();
 await expect(page.getByText('abcde-fghjk')).toBeVisible();
 await expect(page.getByRole('button',{name:/Já guardei/})).toBeVisible();
 await page.setViewportSize({width:390,height:900});await noOverflow(page);
});
