/* OpenWrt Studio theme controls. No telemetry or external resources. */
(function(){
 'use strict';
 var key='openwrt-studio-palette',root=document.documentElement,choice;
 try{choice=localStorage.getItem(key);}catch(e){}
 function apply(){root.setAttribute('data-darkmode',choice==='dark'?'true':choice==='light'?'false':String(matchMedia('(prefers-color-scheme: dark)').matches));}
 apply();matchMedia('(prefers-color-scheme: dark)').addEventListener('change',apply);
 document.addEventListener('DOMContentLoaded',function(){
  var header=document.querySelector('header');if(!header)return;
  var button=document.createElement('button');button.className='cbi-button';button.type='button';button.textContent='◐';button.title='Сменить светлую / тёмную тему';button.setAttribute('aria-label',button.title);
  button.addEventListener('click',function(){choice=root.getAttribute('data-darkmode')==='true'?'light':'dark';try{localStorage.setItem(key,choice);}catch(e){}apply();});
  header.appendChild(button);
 });
 document.addEventListener('keydown',function(event){
  if(!event.ctrlKey||event.altKey||event.metaKey)return;
  var routes={'1':'admin/studio/overview','2':'admin/studio/overview','3':'admin/studio/diagnostics',',':'admin/studio/tools'},route=routes[event.key];
  if(route&&window.L){event.preventDefault();location.href=L.url(route)+(event.key==='2'?'#studio-clients':'');}
 });
})();
