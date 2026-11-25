import fs from 'node:fs'; import path from 'node:path'; import http from 'node:http'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser');
const types={'.js':'text/javascript','.css':'text/css','.html':'text/html','.woff2':'font/woff2','.svg':'image/svg+xml'};
const server = http.createServer((q,r)=>{let p=path.join(dist,decodeURIComponent(q.url.split('?')[0])); if(!fs.existsSync(p)||fs.statSync(p).isDirectory())p=path.join(dist,'index.html'); r.writeHead(200,{'content-type':types[path.extname(p)]??'application/octet-stream'}); fs.createReadStream(p).pipe(r);}).listen(4301);
const b = await puppeteer.launch({executablePath:'/usr/bin/google-chrome',headless:'new',args:['--no-sandbox']});
for (const lang of ['en','ar']) {
const pg = await b.newPage(); await pg.setRequestInterception(true); pg.on('request',rq=>rq.url().startsWith('http://localhost:4301')?rq.continue():rq.respond({status:404,body:''}));
await pg.evaluateOnNewDocument(l=>localStorage.setItem('CapacitorStorage.lang',l),lang);
await pg.goto('http://localhost:4301/login',{waitUntil:'networkidle0'}); await new Promise(r=>setTimeout(r,800));
const res = await pg.evaluate(async()=>{ await document.fonts.ready; const loaded=[...document.fonts].filter(f=>f.status==='loaded').map(f=>f.family+' '+f.unicodeRange.slice(0,20));
 const cs=s=>{const e=document.querySelector(s);return e?getComputedStyle(e).fontFamily.slice(0,60):null};
 return {loaded, body:cs('body'), h1:cs('h1'), input:cs('ion-input input'), btn:cs('ion-button'), check:{manrope:document.fonts.check('16px "Manrope Variable"','Abc'), readex:document.fonts.check('16px "Readex Pro Variable"','مرحبا'), fraunces:document.fonts.check('600 40px "Fraunces Variable"','Sign')}}});
console.log(lang, JSON.stringify(res,null,1)); }
await b.close(); server.close();
