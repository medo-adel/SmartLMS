import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser');
const types = { '.js':'text/javascript', '.css':'text/css', '.html':'text/html' };
const server = http.createServer((q,r)=>{ let p=path.join(dist, decodeURIComponent(q.url.split('?')[0])); if(!fs.existsSync(p)||fs.statSync(p).isDirectory()) p=path.join(dist,'index.html'); r.writeHead(200,{'content-type':types[path.extname(p)]??'application/octet-stream'}); fs.createReadStream(p).pipe(r); }).listen(4316);
const ok=(d)=>({success:true,data:d,message:null,errors:[]});
const TID='11111111-1111-1111-1111-111111111111';
const instructor = { id:'u-in', tenantId:TID, fullName:'Sara Adel', email:'sara@demo.test', phoneNumber:null, preferredLanguage:'en', isActive:true, emailConfirmed:true, roles:['Instructor'], lastLoginAtUtc:null };
const fx = {
 'GET /tenants/current/branding': ok({ name:'Nile', slug:'demo', logoUrl:null, primaryColor:'#0f766e', secondaryColor:'#f59e0b', accentColor:'#7c3aed', defaultLanguage:'en', featureFlags:[] }),
 'GET /me/devices': ok([]), 'GET /notifications': ok([]),
 'GET /courses/mine': ok([{ id:'c1', title:'Algebra II', status:'Draft', ownerInstructorId:'u-in' }]),
 'GET /courses/c1': ok({ id:'c1', title:'Algebra II', slug:'a', shortDescription:'', description:null, status:'Draft', level:'Beginner', language:'en', estimatedHours:1, pricingModel:'Free', price:null, currency:'EGP', ownerInstructorId:'u-in', sections:[] }),
 'GET /courses/c1/question-banks': ok([{ id:'qb1', name:'Algebra bank', topic:null, questionCount:2 }]),
 'GET /question-banks/qb1/questions': ok([{ id:'q1', questionType:'McqSingle', text:'1+1?', defaultPoints:1, difficulty:'Easy', topic:null, explanation:null, options:[] }]),
 'GET /courses/c1/exams': ok([{ id:'e1', title:'Quiz 1', kind:'Quiz', durationMinutes:20, maxAttempts:2, passingScorePercent:60, isPublished:false, availableFromUtc:null, availableUntilUtc:null }]),
 'GET /exams/e1': ok({ id:'e1', courseId:'c1', title:'Quiz 1', kind:'Quiz', instructions:'Show your work', durationMinutes:20, maxAttempts:2, passingScorePercent:60, availableFromUtc:null, availableUntilUtc:null, tabSwitchDetection:true, maxTabSwitches:3, isPublished:false, locked:false }),
};
const browser = await puppeteer.launch({ executablePath:'/usr/bin/google-chrome', headless:'new', args:['--no-sandbox'] });
const p = await (await browser.createBrowserContext()).newPage(); await p.setViewport({width:1280,height:900});
await p.setRequestInterception(true);
p.on('request',(rq)=>{ if(!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
 const u=new URL(rq.url()); const pp=u.pathname.replace('/api/v1','');
 const h={'access-control-allow-origin':'*','access-control-allow-headers':'*','access-control-allow-methods':'*','content-type':'application/json'};
 if(rq.method()==='OPTIONS') return rq.respond({status:204,headers:h});
 const f=fx[rq.method()+' '+pp] ?? fx['GET '+pp];
 return rq.respond({status:200,headers:h,body:JSON.stringify(f??ok({id:'x'}))}); });
await p.evaluateOnNewDocument((u)=>{ localStorage.setItem('CapacitorStorage.lang','en'); localStorage.setItem('CapacitorStorage.tenantKey','demo'); localStorage.setItem('CapacitorStorage.session', JSON.stringify({accessToken:'a',refreshToken:'r',user:u})); }, instructor);
const sleep=(ms)=>new Promise(r=>setTimeout(r,ms));
const vals=()=>p.evaluate(()=>[...document.querySelectorAll('input')].filter(i=>i.getAttribute('name')&&i.getAttribute('name').startsWith('o')).map(i=>[i.getAttribute('name'), i.value, i.type, i.checked]));
await p.goto('http://127.0.0.1:4316/app/assessments',{waitUntil:'networkidle0'}); await sleep(1200);
await p.evaluate(()=>{const s=document.querySelector('select[name="course"]'); s.value='c1'; s.dispatchEvent(new Event('change',{bubbles:true}));}); await sleep(1000);
await p.evaluate(()=>{const s=document.querySelector('select[name="bank"]'); if(s){s.value='qb1'; s.dispatchEvent(new Event('change',{bubbles:true}));}}); await sleep(900);
console.log('after bank pick:', JSON.stringify(await vals()));
await p.evaluate(()=>{[...document.querySelectorAll('ion-button')].find(e=>(e.textContent||'').trim()==='Edit')?.click();}); await sleep(900);
console.log('after edit click:', JSON.stringify(await vals()));
await browser.close(); server.close();
