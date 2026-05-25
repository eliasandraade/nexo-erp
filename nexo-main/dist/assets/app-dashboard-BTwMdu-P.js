import{r as b,u as He,j as s,L as ee}from"./vendor-react-D59skHcJ.js";import{u as R,a as q,b as L}from"./vendor-query-Dv0wELCi.js";import{R as Ee,A as $e,C as Be,X as Oe,Y as Fe,T as De,a as Ue}from"./vendor-charts-LoPeF6LF.js";import{u as Ge,a as Ke}from"./app-restaurante-DXzAD9CI.js";function Me(e){var t,r,a="";if(typeof e=="string"||typeof e=="number")a+=e;else if(typeof e=="object")if(Array.isArray(e)){var i=e.length;for(t=0;t<i;t++)e[t]&&(r=Me(e[t]))&&(a&&(a+=" "),a+=r)}else for(r in e)e[r]&&(a&&(a+=" "),a+=r);return a}function _e(){for(var e,t,r=0,a="",i=arguments.length;r<i;r++)(e=arguments[r])&&(t=Me(e))&&(a&&(a+=" "),a+=t);return a}const ue="-",We=e=>{const t=Ze(e),{conflictingClassGroups:r,conflictingClassGroupModifiers:a}=e;return{getClassGroupId:l=>{const c=l.split(ue);return c[0]===""&&c.length!==1&&c.shift(),Ce(c,t)||Qe(l)},getConflictingClassGroupIds:(l,c)=>{const d=r[l]||[];return c&&a[l]?[...d,...a[l]]:d}}},Ce=(e,t)=>{var l;if(e.length===0)return t.classGroupId;const r=e[0],a=t.nextPart.get(r),i=a?Ce(e.slice(1),a):void 0;if(i)return i;if(t.validators.length===0)return;const n=e.join(ue);return(l=t.validators.find(({validator:c})=>c(n)))==null?void 0:l.classGroupId},ve=/^\[(.+)\]$/,Qe=e=>{if(ve.test(e)){const t=ve.exec(e)[1],r=t==null?void 0:t.substring(0,t.indexOf(":"));if(r)return"arbitrary.."+r}},Ze=e=>{const{theme:t,prefix:r}=e,a={nextPart:new Map,validators:[]};return Xe(Object.entries(e.classGroups),r).forEach(([n,l])=>{de(l,a,n,t)}),a},de=(e,t,r,a)=>{e.forEach(i=>{if(typeof i=="string"){const n=i===""?t:we(t,i);n.classGroupId=r;return}if(typeof i=="function"){if(Ye(i)){de(i(a),t,r,a);return}t.validators.push({validator:i,classGroupId:r});return}Object.entries(i).forEach(([n,l])=>{de(l,we(t,n),r,a)})})},we=(e,t)=>{let r=e;return t.split(ue).forEach(a=>{r.nextPart.has(a)||r.nextPart.set(a,{nextPart:new Map,validators:[]}),r=r.nextPart.get(a)}),r},Ye=e=>e.isThemeGetter,Xe=(e,t)=>t?e.map(([r,a])=>{const i=a.map(n=>typeof n=="string"?t+n:typeof n=="object"?Object.fromEntries(Object.entries(n).map(([l,c])=>[t+l,c])):n);return[r,i]}):e,Je=e=>{if(e<1)return{get:()=>{},set:()=>{}};let t=0,r=new Map,a=new Map;const i=(n,l)=>{r.set(n,l),t++,t>e&&(t=0,a=r,r=new Map)};return{get(n){let l=r.get(n);if(l!==void 0)return l;if((l=a.get(n))!==void 0)return i(n,l),l},set(n,l){r.has(n)?r.set(n,l):i(n,l)}}},Se="!",et=e=>{const{separator:t,experimentalParseClassName:r}=e,a=t.length===1,i=t[0],n=t.length,l=c=>{const d=[];let h=0,m=0,g;for(let k=0;k<c.length;k++){let j=c[k];if(h===0){if(j===i&&(a||c.slice(k,k+n)===t)){d.push(c.slice(m,k)),m=k+n;continue}if(j==="/"){g=k;continue}}j==="["?h++:j==="]"&&h--}const p=d.length===0?c:c.substring(m),M=p.startsWith(Se),v=M?p.substring(1):p,w=g&&g>m?g-m:void 0;return{modifiers:d,hasImportantModifier:M,baseClassName:v,maybePostfixModifierPosition:w}};return r?c=>r({className:c,parseClassName:l}):l},tt=e=>{if(e.length<=1)return e;const t=[];let r=[];return e.forEach(a=>{a[0]==="["?(t.push(...r.sort(),a),r=[]):r.push(a)}),t.push(...r.sort()),t},rt=e=>({cache:Je(e.cacheSize),parseClassName:et(e),...We(e)}),st=/\s+/,at=(e,t)=>{const{parseClassName:r,getClassGroupId:a,getConflictingClassGroupIds:i}=t,n=[],l=e.trim().split(st);let c="";for(let d=l.length-1;d>=0;d-=1){const h=l[d],{modifiers:m,hasImportantModifier:g,baseClassName:p,maybePostfixModifierPosition:M}=r(h);let v=!!M,w=a(v?p.substring(0,M):p);if(!w){if(!v){c=h+(c.length>0?" "+c:c);continue}if(w=a(p),!w){c=h+(c.length>0?" "+c:c);continue}v=!1}const k=tt(m).join(":"),j=g?k+Se:k,C=j+w;if(n.includes(C))continue;n.push(C);const F=i(w,v);for(let I=0;I<F.length;++I){const Q=F[I];n.push(j+Q)}c=h+(c.length>0?" "+c:c)}return c};function ot(){let e=0,t,r,a="";for(;e<arguments.length;)(t=arguments[e++])&&(r=Ne(t))&&(a&&(a+=" "),a+=r);return a}const Ne=e=>{if(typeof e=="string")return e;let t,r="";for(let a=0;a<e.length;a++)e[a]&&(t=Ne(e[a]))&&(r&&(r+=" "),r+=t);return r};function nt(e,...t){let r,a,i,n=l;function l(d){const h=t.reduce((m,g)=>g(m),e());return r=rt(h),a=r.cache.get,i=r.cache.set,n=c,c(d)}function c(d){const h=a(d);if(h)return h;const m=at(d,r);return i(d,m),m}return function(){return n(ot.apply(null,arguments))}}const y=e=>{const t=r=>r[e]||[];return t.isThemeGetter=!0,t},ze=/^\[(?:([a-z-]+):)?(.+)\]$/i,it=/^\d+\/\d+$/,ct=new Set(["px","full","screen"]),lt=/^(\d+(\.\d+)?)?(xs|sm|md|lg|xl)$/,dt=/\d+(%|px|r?em|[sdl]?v([hwib]|min|max)|pt|pc|in|cm|mm|cap|ch|ex|r?lh|cq(w|h|i|b|min|max))|\b(calc|min|max|clamp)\(.+\)|^0$/,ut=/^(rgba?|hsla?|hwb|(ok)?(lab|lch))\(.+\)$/,ht=/^(inset_)?-?((\d+)?\.?(\d+)[a-z]+|0)_-?((\d+)?\.?(\d+)[a-z]+|0)/,pt=/^(url|image|image-set|cross-fade|element|(repeating-)?(linear|radial|conic)-gradient)\(.+\)$/,z=e=>E(e)||ct.has(e)||it.test(e),P=e=>B(e,"length",vt),E=e=>!!e&&!Number.isNaN(Number(e)),le=e=>B(e,"number",E),U=e=>!!e&&Number.isInteger(Number(e)),yt=e=>e.endsWith("%")&&E(e.slice(0,-1)),u=e=>ze.test(e),T=e=>lt.test(e),mt=new Set(["length","size","percentage"]),gt=e=>B(e,mt,Ae),xt=e=>B(e,"position",Ae),ft=new Set(["image","url"]),kt=e=>B(e,ft,jt),bt=e=>B(e,"",wt),G=()=>!0,B=(e,t,r)=>{const a=ze.exec(e);return a?a[1]?typeof t=="string"?a[1]===t:t.has(a[1]):r(a[2]):!1},vt=e=>dt.test(e)&&!ut.test(e),Ae=()=>!1,wt=e=>ht.test(e),jt=e=>pt.test(e),Mt=()=>{const e=y("colors"),t=y("spacing"),r=y("blur"),a=y("brightness"),i=y("borderColor"),n=y("borderRadius"),l=y("borderSpacing"),c=y("borderWidth"),d=y("contrast"),h=y("grayscale"),m=y("hueRotate"),g=y("invert"),p=y("gap"),M=y("gradientColorStops"),v=y("gradientColorStopPositions"),w=y("inset"),k=y("margin"),j=y("opacity"),C=y("padding"),F=y("saturate"),I=y("scale"),Q=y("sepia"),ye=y("skew"),me=y("space"),ge=y("translate"),oe=()=>["auto","contain","none"],ne=()=>["auto","hidden","clip","visible","scroll"],ie=()=>["auto",u,t],x=()=>[u,t],xe=()=>["",z,P],Z=()=>["auto",E,u],fe=()=>["bottom","center","left","left-bottom","left-top","right","right-bottom","right-top","top"],Y=()=>["solid","dashed","dotted","double","none"],ke=()=>["normal","multiply","screen","overlay","darken","lighten","color-dodge","color-burn","hard-light","soft-light","difference","exclusion","hue","saturation","color","luminosity"],ce=()=>["start","end","center","between","around","evenly","stretch"],D=()=>["","0",u],be=()=>["auto","avoid","all","avoid-page","page","left","right","column"],N=()=>[E,u];return{cacheSize:500,separator:":",theme:{colors:[G],spacing:[z,P],blur:["none","",T,u],brightness:N(),borderColor:[e],borderRadius:["none","","full",T,u],borderSpacing:x(),borderWidth:xe(),contrast:N(),grayscale:D(),hueRotate:N(),invert:D(),gap:x(),gradientColorStops:[e],gradientColorStopPositions:[yt,P],inset:ie(),margin:ie(),opacity:N(),padding:x(),saturate:N(),scale:N(),sepia:D(),skew:N(),space:x(),translate:x()},classGroups:{aspect:[{aspect:["auto","square","video",u]}],container:["container"],columns:[{columns:[T]}],"break-after":[{"break-after":be()}],"break-before":[{"break-before":be()}],"break-inside":[{"break-inside":["auto","avoid","avoid-page","avoid-column"]}],"box-decoration":[{"box-decoration":["slice","clone"]}],box:[{box:["border","content"]}],display:["block","inline-block","inline","flex","inline-flex","table","inline-table","table-caption","table-cell","table-column","table-column-group","table-footer-group","table-header-group","table-row-group","table-row","flow-root","grid","inline-grid","contents","list-item","hidden"],float:[{float:["right","left","none","start","end"]}],clear:[{clear:["left","right","both","none","start","end"]}],isolation:["isolate","isolation-auto"],"object-fit":[{object:["contain","cover","fill","none","scale-down"]}],"object-position":[{object:[...fe(),u]}],overflow:[{overflow:ne()}],"overflow-x":[{"overflow-x":ne()}],"overflow-y":[{"overflow-y":ne()}],overscroll:[{overscroll:oe()}],"overscroll-x":[{"overscroll-x":oe()}],"overscroll-y":[{"overscroll-y":oe()}],position:["static","fixed","absolute","relative","sticky"],inset:[{inset:[w]}],"inset-x":[{"inset-x":[w]}],"inset-y":[{"inset-y":[w]}],start:[{start:[w]}],end:[{end:[w]}],top:[{top:[w]}],right:[{right:[w]}],bottom:[{bottom:[w]}],left:[{left:[w]}],visibility:["visible","invisible","collapse"],z:[{z:["auto",U,u]}],basis:[{basis:ie()}],"flex-direction":[{flex:["row","row-reverse","col","col-reverse"]}],"flex-wrap":[{flex:["wrap","wrap-reverse","nowrap"]}],flex:[{flex:["1","auto","initial","none",u]}],grow:[{grow:D()}],shrink:[{shrink:D()}],order:[{order:["first","last","none",U,u]}],"grid-cols":[{"grid-cols":[G]}],"col-start-end":[{col:["auto",{span:["full",U,u]},u]}],"col-start":[{"col-start":Z()}],"col-end":[{"col-end":Z()}],"grid-rows":[{"grid-rows":[G]}],"row-start-end":[{row:["auto",{span:[U,u]},u]}],"row-start":[{"row-start":Z()}],"row-end":[{"row-end":Z()}],"grid-flow":[{"grid-flow":["row","col","dense","row-dense","col-dense"]}],"auto-cols":[{"auto-cols":["auto","min","max","fr",u]}],"auto-rows":[{"auto-rows":["auto","min","max","fr",u]}],gap:[{gap:[p]}],"gap-x":[{"gap-x":[p]}],"gap-y":[{"gap-y":[p]}],"justify-content":[{justify:["normal",...ce()]}],"justify-items":[{"justify-items":["start","end","center","stretch"]}],"justify-self":[{"justify-self":["auto","start","end","center","stretch"]}],"align-content":[{content:["normal",...ce(),"baseline"]}],"align-items":[{items:["start","end","center","baseline","stretch"]}],"align-self":[{self:["auto","start","end","center","stretch","baseline"]}],"place-content":[{"place-content":[...ce(),"baseline"]}],"place-items":[{"place-items":["start","end","center","baseline","stretch"]}],"place-self":[{"place-self":["auto","start","end","center","stretch"]}],p:[{p:[C]}],px:[{px:[C]}],py:[{py:[C]}],ps:[{ps:[C]}],pe:[{pe:[C]}],pt:[{pt:[C]}],pr:[{pr:[C]}],pb:[{pb:[C]}],pl:[{pl:[C]}],m:[{m:[k]}],mx:[{mx:[k]}],my:[{my:[k]}],ms:[{ms:[k]}],me:[{me:[k]}],mt:[{mt:[k]}],mr:[{mr:[k]}],mb:[{mb:[k]}],ml:[{ml:[k]}],"space-x":[{"space-x":[me]}],"space-x-reverse":["space-x-reverse"],"space-y":[{"space-y":[me]}],"space-y-reverse":["space-y-reverse"],w:[{w:["auto","min","max","fit","svw","lvw","dvw",u,t]}],"min-w":[{"min-w":[u,t,"min","max","fit"]}],"max-w":[{"max-w":[u,t,"none","full","min","max","fit","prose",{screen:[T]},T]}],h:[{h:[u,t,"auto","min","max","fit","svh","lvh","dvh"]}],"min-h":[{"min-h":[u,t,"min","max","fit","svh","lvh","dvh"]}],"max-h":[{"max-h":[u,t,"min","max","fit","svh","lvh","dvh"]}],size:[{size:[u,t,"auto","min","max","fit"]}],"font-size":[{text:["base",T,P]}],"font-smoothing":["antialiased","subpixel-antialiased"],"font-style":["italic","not-italic"],"font-weight":[{font:["thin","extralight","light","normal","medium","semibold","bold","extrabold","black",le]}],"font-family":[{font:[G]}],"fvn-normal":["normal-nums"],"fvn-ordinal":["ordinal"],"fvn-slashed-zero":["slashed-zero"],"fvn-figure":["lining-nums","oldstyle-nums"],"fvn-spacing":["proportional-nums","tabular-nums"],"fvn-fraction":["diagonal-fractions","stacked-fractions"],tracking:[{tracking:["tighter","tight","normal","wide","wider","widest",u]}],"line-clamp":[{"line-clamp":["none",E,le]}],leading:[{leading:["none","tight","snug","normal","relaxed","loose",z,u]}],"list-image":[{"list-image":["none",u]}],"list-style-type":[{list:["none","disc","decimal",u]}],"list-style-position":[{list:["inside","outside"]}],"placeholder-color":[{placeholder:[e]}],"placeholder-opacity":[{"placeholder-opacity":[j]}],"text-alignment":[{text:["left","center","right","justify","start","end"]}],"text-color":[{text:[e]}],"text-opacity":[{"text-opacity":[j]}],"text-decoration":["underline","overline","line-through","no-underline"],"text-decoration-style":[{decoration:[...Y(),"wavy"]}],"text-decoration-thickness":[{decoration:["auto","from-font",z,P]}],"underline-offset":[{"underline-offset":["auto",z,u]}],"text-decoration-color":[{decoration:[e]}],"text-transform":["uppercase","lowercase","capitalize","normal-case"],"text-overflow":["truncate","text-ellipsis","text-clip"],"text-wrap":[{text:["wrap","nowrap","balance","pretty"]}],indent:[{indent:x()}],"vertical-align":[{align:["baseline","top","middle","bottom","text-top","text-bottom","sub","super",u]}],whitespace:[{whitespace:["normal","nowrap","pre","pre-line","pre-wrap","break-spaces"]}],break:[{break:["normal","words","all","keep"]}],hyphens:[{hyphens:["none","manual","auto"]}],content:[{content:["none",u]}],"bg-attachment":[{bg:["fixed","local","scroll"]}],"bg-clip":[{"bg-clip":["border","padding","content","text"]}],"bg-opacity":[{"bg-opacity":[j]}],"bg-origin":[{"bg-origin":["border","padding","content"]}],"bg-position":[{bg:[...fe(),xt]}],"bg-repeat":[{bg:["no-repeat",{repeat:["","x","y","round","space"]}]}],"bg-size":[{bg:["auto","cover","contain",gt]}],"bg-image":[{bg:["none",{"gradient-to":["t","tr","r","br","b","bl","l","tl"]},kt]}],"bg-color":[{bg:[e]}],"gradient-from-pos":[{from:[v]}],"gradient-via-pos":[{via:[v]}],"gradient-to-pos":[{to:[v]}],"gradient-from":[{from:[M]}],"gradient-via":[{via:[M]}],"gradient-to":[{to:[M]}],rounded:[{rounded:[n]}],"rounded-s":[{"rounded-s":[n]}],"rounded-e":[{"rounded-e":[n]}],"rounded-t":[{"rounded-t":[n]}],"rounded-r":[{"rounded-r":[n]}],"rounded-b":[{"rounded-b":[n]}],"rounded-l":[{"rounded-l":[n]}],"rounded-ss":[{"rounded-ss":[n]}],"rounded-se":[{"rounded-se":[n]}],"rounded-ee":[{"rounded-ee":[n]}],"rounded-es":[{"rounded-es":[n]}],"rounded-tl":[{"rounded-tl":[n]}],"rounded-tr":[{"rounded-tr":[n]}],"rounded-br":[{"rounded-br":[n]}],"rounded-bl":[{"rounded-bl":[n]}],"border-w":[{border:[c]}],"border-w-x":[{"border-x":[c]}],"border-w-y":[{"border-y":[c]}],"border-w-s":[{"border-s":[c]}],"border-w-e":[{"border-e":[c]}],"border-w-t":[{"border-t":[c]}],"border-w-r":[{"border-r":[c]}],"border-w-b":[{"border-b":[c]}],"border-w-l":[{"border-l":[c]}],"border-opacity":[{"border-opacity":[j]}],"border-style":[{border:[...Y(),"hidden"]}],"divide-x":[{"divide-x":[c]}],"divide-x-reverse":["divide-x-reverse"],"divide-y":[{"divide-y":[c]}],"divide-y-reverse":["divide-y-reverse"],"divide-opacity":[{"divide-opacity":[j]}],"divide-style":[{divide:Y()}],"border-color":[{border:[i]}],"border-color-x":[{"border-x":[i]}],"border-color-y":[{"border-y":[i]}],"border-color-s":[{"border-s":[i]}],"border-color-e":[{"border-e":[i]}],"border-color-t":[{"border-t":[i]}],"border-color-r":[{"border-r":[i]}],"border-color-b":[{"border-b":[i]}],"border-color-l":[{"border-l":[i]}],"divide-color":[{divide:[i]}],"outline-style":[{outline:["",...Y()]}],"outline-offset":[{"outline-offset":[z,u]}],"outline-w":[{outline:[z,P]}],"outline-color":[{outline:[e]}],"ring-w":[{ring:xe()}],"ring-w-inset":["ring-inset"],"ring-color":[{ring:[e]}],"ring-opacity":[{"ring-opacity":[j]}],"ring-offset-w":[{"ring-offset":[z,P]}],"ring-offset-color":[{"ring-offset":[e]}],shadow:[{shadow:["","inner","none",T,bt]}],"shadow-color":[{shadow:[G]}],opacity:[{opacity:[j]}],"mix-blend":[{"mix-blend":[...ke(),"plus-lighter","plus-darker"]}],"bg-blend":[{"bg-blend":ke()}],filter:[{filter:["","none"]}],blur:[{blur:[r]}],brightness:[{brightness:[a]}],contrast:[{contrast:[d]}],"drop-shadow":[{"drop-shadow":["","none",T,u]}],grayscale:[{grayscale:[h]}],"hue-rotate":[{"hue-rotate":[m]}],invert:[{invert:[g]}],saturate:[{saturate:[F]}],sepia:[{sepia:[Q]}],"backdrop-filter":[{"backdrop-filter":["","none"]}],"backdrop-blur":[{"backdrop-blur":[r]}],"backdrop-brightness":[{"backdrop-brightness":[a]}],"backdrop-contrast":[{"backdrop-contrast":[d]}],"backdrop-grayscale":[{"backdrop-grayscale":[h]}],"backdrop-hue-rotate":[{"backdrop-hue-rotate":[m]}],"backdrop-invert":[{"backdrop-invert":[g]}],"backdrop-opacity":[{"backdrop-opacity":[j]}],"backdrop-saturate":[{"backdrop-saturate":[F]}],"backdrop-sepia":[{"backdrop-sepia":[Q]}],"border-collapse":[{border:["collapse","separate"]}],"border-spacing":[{"border-spacing":[l]}],"border-spacing-x":[{"border-spacing-x":[l]}],"border-spacing-y":[{"border-spacing-y":[l]}],"table-layout":[{table:["auto","fixed"]}],caption:[{caption:["top","bottom"]}],transition:[{transition:["none","all","","colors","opacity","shadow","transform",u]}],duration:[{duration:N()}],ease:[{ease:["linear","in","out","in-out",u]}],delay:[{delay:N()}],animate:[{animate:["none","spin","ping","pulse","bounce",u]}],transform:[{transform:["","gpu","none"]}],scale:[{scale:[I]}],"scale-x":[{"scale-x":[I]}],"scale-y":[{"scale-y":[I]}],rotate:[{rotate:[U,u]}],"translate-x":[{"translate-x":[ge]}],"translate-y":[{"translate-y":[ge]}],"skew-x":[{"skew-x":[ye]}],"skew-y":[{"skew-y":[ye]}],"transform-origin":[{origin:["center","top","top-right","right","bottom-right","bottom","bottom-left","left","top-left",u]}],accent:[{accent:["auto",e]}],appearance:[{appearance:["none","auto"]}],cursor:[{cursor:["auto","default","pointer","wait","text","move","help","not-allowed","none","context-menu","progress","cell","crosshair","vertical-text","alias","copy","no-drop","grab","grabbing","all-scroll","col-resize","row-resize","n-resize","e-resize","s-resize","w-resize","ne-resize","nw-resize","se-resize","sw-resize","ew-resize","ns-resize","nesw-resize","nwse-resize","zoom-in","zoom-out",u]}],"caret-color":[{caret:[e]}],"pointer-events":[{"pointer-events":["none","auto"]}],resize:[{resize:["none","y","x",""]}],"scroll-behavior":[{scroll:["auto","smooth"]}],"scroll-m":[{"scroll-m":x()}],"scroll-mx":[{"scroll-mx":x()}],"scroll-my":[{"scroll-my":x()}],"scroll-ms":[{"scroll-ms":x()}],"scroll-me":[{"scroll-me":x()}],"scroll-mt":[{"scroll-mt":x()}],"scroll-mr":[{"scroll-mr":x()}],"scroll-mb":[{"scroll-mb":x()}],"scroll-ml":[{"scroll-ml":x()}],"scroll-p":[{"scroll-p":x()}],"scroll-px":[{"scroll-px":x()}],"scroll-py":[{"scroll-py":x()}],"scroll-ps":[{"scroll-ps":x()}],"scroll-pe":[{"scroll-pe":x()}],"scroll-pt":[{"scroll-pt":x()}],"scroll-pr":[{"scroll-pr":x()}],"scroll-pb":[{"scroll-pb":x()}],"scroll-pl":[{"scroll-pl":x()}],"snap-align":[{snap:["start","end","center","align-none"]}],"snap-stop":[{snap:["normal","always"]}],"snap-type":[{snap:["none","x","y","both"]}],"snap-strictness":[{snap:["mandatory","proximity"]}],touch:[{touch:["auto","none","manipulation"]}],"touch-x":[{"touch-pan":["x","left","right"]}],"touch-y":[{"touch-pan":["y","up","down"]}],"touch-pz":["touch-pinch-zoom"],select:[{select:["none","text","all","auto"]}],"will-change":[{"will-change":["auto","scroll","contents","transform",u]}],fill:[{fill:[e,"none"]}],"stroke-w":[{stroke:[z,P,le]}],stroke:[{stroke:[e,"none"]}],sr:["sr-only","not-sr-only"],"forced-color-adjust":[{"forced-color-adjust":["auto","none"]}]},conflictingClassGroups:{overflow:["overflow-x","overflow-y"],overscroll:["overscroll-x","overscroll-y"],inset:["inset-x","inset-y","start","end","top","right","bottom","left"],"inset-x":["right","left"],"inset-y":["top","bottom"],flex:["basis","grow","shrink"],gap:["gap-x","gap-y"],p:["px","py","ps","pe","pt","pr","pb","pl"],px:["pr","pl"],py:["pt","pb"],m:["mx","my","ms","me","mt","mr","mb","ml"],mx:["mr","ml"],my:["mt","mb"],size:["w","h"],"font-size":["leading"],"fvn-normal":["fvn-ordinal","fvn-slashed-zero","fvn-figure","fvn-spacing","fvn-fraction"],"fvn-ordinal":["fvn-normal"],"fvn-slashed-zero":["fvn-normal"],"fvn-figure":["fvn-normal"],"fvn-spacing":["fvn-normal"],"fvn-fraction":["fvn-normal"],"line-clamp":["display","overflow"],rounded:["rounded-s","rounded-e","rounded-t","rounded-r","rounded-b","rounded-l","rounded-ss","rounded-se","rounded-ee","rounded-es","rounded-tl","rounded-tr","rounded-br","rounded-bl"],"rounded-s":["rounded-ss","rounded-es"],"rounded-e":["rounded-se","rounded-ee"],"rounded-t":["rounded-tl","rounded-tr"],"rounded-r":["rounded-tr","rounded-br"],"rounded-b":["rounded-br","rounded-bl"],"rounded-l":["rounded-tl","rounded-bl"],"border-spacing":["border-spacing-x","border-spacing-y"],"border-w":["border-w-s","border-w-e","border-w-t","border-w-r","border-w-b","border-w-l"],"border-w-x":["border-w-r","border-w-l"],"border-w-y":["border-w-t","border-w-b"],"border-color":["border-color-s","border-color-e","border-color-t","border-color-r","border-color-b","border-color-l"],"border-color-x":["border-color-r","border-color-l"],"border-color-y":["border-color-t","border-color-b"],"scroll-m":["scroll-mx","scroll-my","scroll-ms","scroll-me","scroll-mt","scroll-mr","scroll-mb","scroll-ml"],"scroll-mx":["scroll-mr","scroll-ml"],"scroll-my":["scroll-mt","scroll-mb"],"scroll-p":["scroll-px","scroll-py","scroll-ps","scroll-pe","scroll-pt","scroll-pr","scroll-pb","scroll-pl"],"scroll-px":["scroll-pr","scroll-pl"],"scroll-py":["scroll-pt","scroll-pb"],touch:["touch-x","touch-y","touch-pz"],"touch-x":["touch"],"touch-y":["touch"],"touch-pz":["touch"]},conflictingClassGroupModifiers:{"font-size":["leading"]}}},Ct=nt(Mt);function _(...e){return Ct(_e(e))}const S={access:"nexo:access_token",refresh:"nexo:refresh_token",session:"nexo:session"},he="https://backend-production-b2bc.up.railway.app/api";let pe=null;function qe(){return pe??localStorage.getItem(S.access)}function te(e,t){e&&(pe=e,localStorage.setItem(S.access,e)),t&&localStorage.setItem(S.refresh,t)}function $(){pe=null,localStorage.removeItem(S.access),localStorage.removeItem(S.refresh),localStorage.removeItem(S.session)}let K=null;async function Le(){return K||(K=(async()=>{try{const e=localStorage.getItem(S.refresh);if(!e)return!1;const t=await fetch(`${he}/auth/refresh`,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({refreshToken:e})});if(!t.ok)return $(),!1;const r=await t.json();return r.accessToken&&te(r.accessToken,r.refreshToken??""),!0}catch(e){return console.error("Token refresh failed:",e),$(),!1}finally{K=null}})(),K)}async function H(e,t,r,a=!1){const i=`${he}${t}`,n={"Content-Type":"application/json"},l=qe();l&&(n.Authorization=`Bearer ${l}`);const c=await fetch(i,{method:e,headers:n,body:r!==void 0?JSON.stringify(r):void 0});if(c.status===401&&!a){if(await Le())return H(e,t,r,!0);throw $(),new X(401,"Unauthorized")}if(!c.ok){let d=c.statusText;try{const h=await c.json();Array.isArray(h==null?void 0:h.details)&&h.details.length>0?d=h.details.join(" | "):d=(h==null?void 0:h.error)??(h==null?void 0:h.message)??d}catch{}throw new X(c.status,d)}if(c.status!==204)return c.json()}async function Pe(e,t){const r=`${he}${e}`,a={},i=qe();i&&(a.Authorization=`Bearer ${i}`);const n=await fetch(r,{method:"POST",headers:a,body:t});if(n.status===401){if(await Le())return Pe(e,t);throw $(),new X(401,"Unauthorized")}if(!n.ok){let l=n.statusText;try{const c=await n.json();Array.isArray(c==null?void 0:c.details)&&c.details.length>0?l=c.details.join(" | "):l=(c==null?void 0:c.error)??(c==null?void 0:c.message)??l}catch{}throw new X(n.status,l)}if(n.status!==204)return n.json()}const f={get:e=>H("GET",e),post:(e,t)=>H("POST",e,t),put:(e,t)=>H("PUT",e,t),patch:(e,t)=>H("PATCH",e,t),delete:e=>H("DELETE",e),postForm:(e,t)=>Pe(e,t)};class X extends Error{constructor(t,r){super(r),this.status=t,this.name="ApiError"}}function re(e){return{userId:e.userId,tenantId:e.tenantId,name:e.name,role:e.role,login:e.login,email:e.email,modules:e.activeModules??[],storeId:e.storeId,storeIds:e.storeIds??[],companyName:e.companyName??"",type:e.type==="platform"?"platform":"tenant",isNewAccount:e.isNewAccount??!1}}function se(e){localStorage.setItem(S.session,JSON.stringify(e))}function je(){try{const e=localStorage.getItem(S.session);return e?JSON.parse(e):null}catch{return null}}async function St(e){try{const t=await f.post("/auth/login",{login:e.login,password:e.password}),r=re(t.session);return te(t.accessToken,t.refreshToken),se(r),{success:!0,session:r}}catch(t){const r=t instanceof Error?t.message:"Erro ao conectar com o servidor.";return r==="Unauthorized"||r.includes("401")?{success:!1,error:"Login ou senha incorretos."}:{success:!1,error:r}}}async function Nt(){try{const e=localStorage.getItem(S.refresh);f.post("/auth/logout",{refreshToken:e??""}).catch(()=>{})}finally{$()}}async function zt(e){const t=localStorage.getItem(S.refresh)??"",r=await f.post("/auth/switch-store",{storeId:e,refreshToken:t}),a=re(r.session);return te(r.accessToken,r.refreshToken),se(a),a}async function At(){try{const e=await f.get("/auth/me"),t=re(e);return se(t),t}catch{return $(),null}}async function Hr(e){try{return await f.post("/auth/register",{name:e.name,email:e.email,password:e.password}),{success:!0}}catch(t){const r=t instanceof Error?t.message:"Erro ao criar conta.";return r.includes("email_already_registered")||r.includes("409")?{success:!1,error:"Este e-mail já está cadastrado."}:{success:!1,error:r}}}async function Er(e){try{const t=await f.get(`/auth/verify-email?token=${encodeURIComponent(e)}`),r=re(t.session);return te(t.accessToken,t.refreshToken),se(r),r.isNewAccount&&localStorage.setItem(`nexo:onboarding:${r.userId}`,"true"),{success:!0,session:r}}catch{return{success:!1,error:"Link inválido ou expirado."}}}async function $r(e){await f.post("/auth/resend-verification",{email:e})}const Te=b.createContext(null);function Br({children:e}){const[t,r]=b.useState(()=>je()),[a,i]=b.useState(!1),n=He();b.useEffect(()=>{if(!je()){i(!0);return}At().then(p=>{p?(r(p),p.type==="platform"&&!window.location.pathname.startsWith("/platform")&&n("/platform",{replace:!0})):(r(null),n("/login",{replace:!0})),i(!0)})},[]);const l=b.useCallback(async g=>{const p=await St(g);return p.success?(r(p.session),{error:null,type:p.session.type}):{error:p.error,type:null}},[]),c=b.useCallback(()=>{Nt(),r(null),n("/login",{replace:!0})},[n]),d=b.useCallback(async g=>{const p=await zt(g);r(p)},[]),h=b.useCallback(g=>{r(g)},[]),m=b.useMemo(()=>({session:t,isReady:a,login:l,logout:c,switchStore:d,setSessionFromVerify:h}),[t,a,l,c,d,h]);return s.jsx(Te.Provider,{value:m,children:e})}function Ie(){const e=b.useContext(Te);if(!e)throw new Error("useAuth must be used inside AuthProvider");return e}/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const qt=e=>e.replace(/([a-z0-9])([A-Z])/g,"$1-$2").toLowerCase(),Re=(...e)=>e.filter((t,r,a)=>!!t&&t.trim()!==""&&a.indexOf(t)===r).join(" ").trim();/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */var Lt={xmlns:"http://www.w3.org/2000/svg",width:24,height:24,viewBox:"0 0 24 24",fill:"none",stroke:"currentColor",strokeWidth:2,strokeLinecap:"round",strokeLinejoin:"round"};/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Pt=b.forwardRef(({color:e="currentColor",size:t=24,strokeWidth:r=2,absoluteStrokeWidth:a,className:i="",children:n,iconNode:l,...c},d)=>b.createElement("svg",{ref:d,...Lt,width:t,height:t,stroke:e,strokeWidth:a?Number(r)*24/Number(t):r,className:Re("lucide",i),...c},[...l.map(([h,m])=>b.createElement(h,m)),...Array.isArray(n)?n:[n]]));/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const o=(e,t)=>{const r=b.forwardRef(({className:a,...i},n)=>b.createElement(Pt,{ref:n,iconNode:t,className:Re(`lucide-${qt(e)}`,a),...i}));return r.displayName=`${e}`,r};/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Tt=o("Activity",[["path",{d:"M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2",key:"169zse"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Or=o("ArrowDownLeft",[["path",{d:"M17 7 7 17",key:"15tmo1"}],["path",{d:"M17 17H7V7",key:"1org7z"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Fr=o("ArrowDown",[["path",{d:"M12 5v14",key:"s699le"}],["path",{d:"m19 12-7 7-7-7",key:"1idqje"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Dr=o("ArrowLeft",[["path",{d:"m12 19-7-7 7-7",key:"1l729n"}],["path",{d:"M19 12H5",key:"x3x0zl"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ur=o("ArrowUpDown",[["path",{d:"m21 16-4 4-4-4",key:"f6ql7i"}],["path",{d:"M17 20V4",key:"1ejh1v"}],["path",{d:"m3 8 4-4 4 4",key:"11wl7u"}],["path",{d:"M7 4v16",key:"1glfcx"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Gr=o("ArrowUpRight",[["path",{d:"M7 7h10v10",key:"1tivn9"}],["path",{d:"M7 17 17 7",key:"1vkiza"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Kr=o("ArrowUp",[["path",{d:"m5 12 7-7 7 7",key:"hav0vg"}],["path",{d:"M12 19V5",key:"x0mq9r"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const _r=o("Ban",[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"m4.9 4.9 14.2 14.2",key:"1m5liu"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Wr=o("Banknote",[["rect",{width:"20",height:"12",x:"2",y:"6",rx:"2",key:"9lu3g6"}],["circle",{cx:"12",cy:"12",r:"2",key:"1c9p78"}],["path",{d:"M6 12h.01M18 12h.01",key:"113zkx"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Qr=o("Bell",[["path",{d:"M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9",key:"1qo2s2"}],["path",{d:"M10.3 21a1.94 1.94 0 0 0 3.4 0",key:"qgo35s"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Zr=o("Bike",[["circle",{cx:"18.5",cy:"17.5",r:"3.5",key:"15x4ox"}],["circle",{cx:"5.5",cy:"17.5",r:"3.5",key:"1noe27"}],["circle",{cx:"15",cy:"5",r:"1",key:"19l28e"}],["path",{d:"M12 17.5V14l-3-3 4-3 2 3h2",key:"1npguv"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Yr=o("BookOpen",[["path",{d:"M12 7v14",key:"1akyts"}],["path",{d:"M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z",key:"ruj8y"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Xr=o("Brain",[["path",{d:"M12 5a3 3 0 1 0-5.997.125 4 4 0 0 0-2.526 5.77 4 4 0 0 0 .556 6.588A4 4 0 1 0 12 18Z",key:"l5xja"}],["path",{d:"M12 5a3 3 0 1 1 5.997.125 4 4 0 0 1 2.526 5.77 4 4 0 0 1-.556 6.588A4 4 0 1 1 12 18Z",key:"ep3f8r"}],["path",{d:"M15 13a4.5 4.5 0 0 1-3-4 4.5 4.5 0 0 1-3 4",key:"1p4c4q"}],["path",{d:"M17.599 6.5a3 3 0 0 0 .399-1.375",key:"tmeiqw"}],["path",{d:"M6.003 5.125A3 3 0 0 0 6.401 6.5",key:"105sqy"}],["path",{d:"M3.477 10.896a4 4 0 0 1 .585-.396",key:"ql3yin"}],["path",{d:"M19.938 10.5a4 4 0 0 1 .585.396",key:"1qfode"}],["path",{d:"M6 18a4 4 0 0 1-1.967-.516",key:"2e4loj"}],["path",{d:"M19.967 17.484A4 4 0 0 1 18 18",key:"159ez6"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Jr=o("Building2",[["path",{d:"M6 22V4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v18Z",key:"1b4qmf"}],["path",{d:"M6 12H4a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h2",key:"i71pzd"}],["path",{d:"M18 9h2a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-2",key:"10jefs"}],["path",{d:"M10 6h4",key:"1itunk"}],["path",{d:"M10 10h4",key:"tcdvrf"}],["path",{d:"M10 14h4",key:"kelpxr"}],["path",{d:"M10 18h4",key:"1ulq68"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const es=o("Calendar",[["path",{d:"M8 2v4",key:"1cmpym"}],["path",{d:"M16 2v4",key:"4m81vk"}],["rect",{width:"18",height:"18",x:"3",y:"4",rx:"2",key:"1hopcy"}],["path",{d:"M3 10h18",key:"8toen8"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ts=o("Camera",[["path",{d:"M14.5 4h-5L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3l-2.5-3z",key:"1tc9qg"}],["circle",{cx:"12",cy:"13",r:"3",key:"1vg3eu"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const rs=o("ChartNoAxesColumn",[["line",{x1:"18",x2:"18",y1:"20",y2:"10",key:"1xfpm4"}],["line",{x1:"12",x2:"12",y1:"20",y2:"4",key:"be30l9"}],["line",{x1:"6",x2:"6",y1:"20",y2:"14",key:"1r4le6"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ss=o("Check",[["path",{d:"M20 6 9 17l-5-5",key:"1gmf2c"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const It=o("ChefHat",[["path",{d:"M17 21a1 1 0 0 0 1-1v-5.35c0-.457.316-.844.727-1.041a4 4 0 0 0-2.134-7.589 5 5 0 0 0-9.186 0 4 4 0 0 0-2.134 7.588c.411.198.727.585.727 1.041V20a1 1 0 0 0 1 1Z",key:"1qvrer"}],["path",{d:"M6 17h12",key:"1jwigz"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const as=o("ChevronDown",[["path",{d:"m6 9 6 6 6-6",key:"qrunsl"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const os=o("ChevronLeft",[["path",{d:"m15 18-6-6 6-6",key:"1wnfg3"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Rt=o("ChevronRight",[["path",{d:"m9 18 6-6-6-6",key:"mthhwq"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ns=o("ChevronUp",[["path",{d:"m18 15-6-6-6 6",key:"153udz"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const is=o("CircleAlert",[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["line",{x1:"12",x2:"12",y1:"8",y2:"12",key:"1pkeuh"}],["line",{x1:"12",x2:"12.01",y1:"16",y2:"16",key:"4dfq90"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const cs=o("CircleCheckBig",[["path",{d:"M21.801 10A10 10 0 1 1 17 3.335",key:"yps3ct"}],["path",{d:"m9 11 3 3L22 4",key:"1pflzl"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Vt=o("CircleCheck",[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"m9 12 2 2 4-4",key:"dzmm74"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ls=o("CircleDollarSign",[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"M16 8h-6a2 2 0 1 0 0 4h4a2 2 0 1 1 0 4H8",key:"1h4pet"}],["path",{d:"M12 18V6",key:"zqpxq5"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ds=o("CircleUser",[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["circle",{cx:"12",cy:"10",r:"3",key:"ilqhr7"}],["path",{d:"M7 20.662V19a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v1.662",key:"154egf"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const us=o("CircleX",[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"m15 9-6 6",key:"1uzhvr"}],["path",{d:"m9 9 6 6",key:"z0biqf"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ht=o("Circle",[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const hs=o("Clock",[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["polyline",{points:"12 6 12 12 16 14",key:"68esgv"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ps=o("Copy",[["rect",{width:"14",height:"14",x:"8",y:"8",rx:"2",ry:"2",key:"17jyea"}],["path",{d:"M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2",key:"zix9uf"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ys=o("CreditCard",[["rect",{width:"20",height:"14",x:"2",y:"5",rx:"2",key:"ynyp8z"}],["line",{x1:"2",x2:"22",y1:"10",y2:"10",key:"1b3vmo"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Et=o("DollarSign",[["line",{x1:"12",x2:"12",y1:"2",y2:"22",key:"7eqyqh"}],["path",{d:"M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6",key:"1b0p4s"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ms=o("Ellipsis",[["circle",{cx:"12",cy:"12",r:"1",key:"41hilf"}],["circle",{cx:"19",cy:"12",r:"1",key:"1wjl8i"}],["circle",{cx:"5",cy:"12",r:"1",key:"1pcz8c"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const gs=o("ExternalLink",[["path",{d:"M15 3h6v6",key:"1q9fwt"}],["path",{d:"M10 14 21 3",key:"gplh6r"}],["path",{d:"M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",key:"a6xqqp"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const xs=o("EyeOff",[["path",{d:"M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49",key:"ct8e1f"}],["path",{d:"M14.084 14.158a3 3 0 0 1-4.242-4.242",key:"151rxh"}],["path",{d:"M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143",key:"13bj9a"}],["path",{d:"m2 2 20 20",key:"1ooewy"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const fs=o("Eye",[["path",{d:"M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0",key:"1nclc0"}],["circle",{cx:"12",cy:"12",r:"3",key:"1v7zrd"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ks=o("FileCode",[["path",{d:"M10 12.5 8 15l2 2.5",key:"1tg20x"}],["path",{d:"m14 12.5 2 2.5-2 2.5",key:"yinavb"}],["path",{d:"M14 2v4a2 2 0 0 0 2 2h4",key:"tnqrlb"}],["path",{d:"M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7z",key:"1mlx9k"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const bs=o("FileText",[["path",{d:"M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z",key:"1rqfz7"}],["path",{d:"M14 2v4a2 2 0 0 0 2 2h4",key:"tnqrlb"}],["path",{d:"M10 9H8",key:"b1mrlr"}],["path",{d:"M16 13H8",key:"t4e002"}],["path",{d:"M16 17H8",key:"z1uh3a"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const vs=o("Filter",[["polygon",{points:"22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3",key:"1yg77f"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ws=o("Flag",[["path",{d:"M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z",key:"i9b6wo"}],["line",{x1:"4",x2:"4",y1:"22",y2:"15",key:"1cm3nv"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const js=o("FlaskConical",[["path",{d:"M10 2v7.527a2 2 0 0 1-.211.896L4.72 20.55a1 1 0 0 0 .9 1.45h12.76a1 1 0 0 0 .9-1.45l-5.069-10.127A2 2 0 0 1 14 9.527V2",key:"pzvekw"}],["path",{d:"M8.5 2h7",key:"csnxdl"}],["path",{d:"M7 16h10",key:"wp8him"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ms=o("GitBranch",[["line",{x1:"6",x2:"6",y1:"3",y2:"15",key:"17qcm7"}],["circle",{cx:"18",cy:"6",r:"3",key:"1h7g24"}],["circle",{cx:"6",cy:"18",r:"3",key:"fqmcym"}],["path",{d:"M18 9a9 9 0 0 1-9 9",key:"n2h4wq"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Cs=o("Globe",[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20",key:"13o1zl"}],["path",{d:"M2 12h20",key:"9i4pu4"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ss=o("HardHat",[["path",{d:"M10 10V5a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1v5",key:"1p9q5i"}],["path",{d:"M14 6a6 6 0 0 1 6 6v3",key:"1hnv84"}],["path",{d:"M4 15v-3a6 6 0 0 1 6-6",key:"9ciidu"}],["rect",{x:"2",y:"15",width:"20",height:"4",rx:"1",key:"g3x8cw"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ns=o("Hash",[["line",{x1:"4",x2:"20",y1:"9",y2:"9",key:"4lhtct"}],["line",{x1:"4",x2:"20",y1:"15",y2:"15",key:"vyu0kd"}],["line",{x1:"10",x2:"8",y1:"3",y2:"21",key:"1ggp8o"}],["line",{x1:"16",x2:"14",y1:"3",y2:"21",key:"weycgp"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const zs=o("History",[["path",{d:"M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8",key:"1357e3"}],["path",{d:"M3 3v5h5",key:"1xhq8a"}],["path",{d:"M12 7v5l4 2",key:"1fdv2h"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const As=o("ImageOff",[["line",{x1:"2",x2:"22",y1:"2",y2:"22",key:"a6p6uj"}],["path",{d:"M10.41 10.41a2 2 0 1 1-2.83-2.83",key:"1bzlo9"}],["line",{x1:"13.5",x2:"6",y1:"13.5",y2:"21",key:"1q0aeu"}],["line",{x1:"18",x2:"21",y1:"12",y2:"15",key:"5mozeu"}],["path",{d:"M3.59 3.59A1.99 1.99 0 0 0 3 5v14a2 2 0 0 0 2 2h14c.55 0 1.052-.22 1.41-.59",key:"mmje98"}],["path",{d:"M21 15V5a2 2 0 0 0-2-2H9",key:"43el77"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const qs=o("ImagePlus",[["path",{d:"M16 5h6",key:"1vod17"}],["path",{d:"M19 2v6",key:"4bpg5p"}],["path",{d:"M21 11.5V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h7.5",key:"1ue2ih"}],["path",{d:"m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21",key:"1xmnt7"}],["circle",{cx:"9",cy:"9",r:"2",key:"af1f0g"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ls=o("Inbox",[["polyline",{points:"22 12 16 12 14 15 10 15 8 12 2 12",key:"o97t9d"}],["path",{d:"M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z",key:"oot6mr"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ps=o("Info",[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"M12 16v-4",key:"1dtifu"}],["path",{d:"M12 8h.01",key:"e9boi3"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ts=o("KeyRound",[["path",{d:"M2.586 17.414A2 2 0 0 0 2 18.828V21a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h1a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h.172a2 2 0 0 0 1.414-.586l.814-.814a6.5 6.5 0 1 0-4-4z",key:"1s6t7t"}],["circle",{cx:"16.5",cy:"7.5",r:".5",fill:"currentColor",key:"w0ekpg"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Is=o("Key",[["path",{d:"m15.5 7.5 2.3 2.3a1 1 0 0 0 1.4 0l2.1-2.1a1 1 0 0 0 0-1.4L19 4",key:"g0fldk"}],["path",{d:"m21 2-9.6 9.6",key:"1j0ho8"}],["circle",{cx:"7.5",cy:"15.5",r:"5.5",key:"yqb3hr"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const $t=o("Landmark",[["line",{x1:"3",x2:"21",y1:"22",y2:"22",key:"j8o0r"}],["line",{x1:"6",x2:"6",y1:"18",y2:"11",key:"10tf0k"}],["line",{x1:"10",x2:"10",y1:"18",y2:"11",key:"54lgf6"}],["line",{x1:"14",x2:"14",y1:"18",y2:"11",key:"380y"}],["line",{x1:"18",x2:"18",y1:"18",y2:"11",key:"1kevvc"}],["polygon",{points:"12 2 20 7 4 7",key:"jkujk7"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Rs=o("LayoutDashboard",[["rect",{width:"7",height:"9",x:"3",y:"3",rx:"1",key:"10lvy0"}],["rect",{width:"7",height:"5",x:"14",y:"3",rx:"1",key:"16une8"}],["rect",{width:"7",height:"9",x:"14",y:"12",rx:"1",key:"1hutg5"}],["rect",{width:"7",height:"5",x:"3",y:"16",rx:"1",key:"ldoo1y"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Bt=o("Lightbulb",[["path",{d:"M15 14c.2-1 .7-1.7 1.5-2.5 1-.9 1.5-2.2 1.5-3.5A6 6 0 0 0 6 8c0 1 .2 2.2 1.5 3.5.7.7 1.3 1.5 1.5 2.5",key:"1gvzjb"}],["path",{d:"M9 18h6",key:"x1upvd"}],["path",{d:"M10 22h4",key:"ceow96"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Vs=o("Link2",[["path",{d:"M9 17H7A5 5 0 0 1 7 7h2",key:"8i5ue5"}],["path",{d:"M15 7h2a5 5 0 1 1 0 10h-2",key:"1b9ql8"}],["line",{x1:"8",x2:"16",y1:"12",y2:"12",key:"1jonct"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Hs=o("List",[["path",{d:"M3 12h.01",key:"nlz23k"}],["path",{d:"M3 18h.01",key:"1tta3j"}],["path",{d:"M3 6h.01",key:"1rqtza"}],["path",{d:"M8 12h13",key:"1za7za"}],["path",{d:"M8 18h13",key:"1lx6n3"}],["path",{d:"M8 6h13",key:"ik3vkj"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Es=o("LoaderCircle",[["path",{d:"M21 12a9 9 0 1 1-6.219-8.56",key:"13zald"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const $s=o("LogOut",[["path",{d:"M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4",key:"1uf3rs"}],["polyline",{points:"16 17 21 12 16 7",key:"1gabdz"}],["line",{x1:"21",x2:"9",y1:"12",y2:"12",key:"1uyos4"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Bs=o("Mail",[["rect",{width:"20",height:"16",x:"2",y:"4",rx:"2",key:"18n3k1"}],["path",{d:"m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7",key:"1ocrg3"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Os=o("MapPin",[["path",{d:"M20 10c0 4.993-5.539 10.193-7.399 11.799a1 1 0 0 1-1.202 0C9.539 20.193 4 14.993 4 10a8 8 0 0 1 16 0",key:"1r0f0z"}],["circle",{cx:"12",cy:"10",r:"3",key:"ilqhr7"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Fs=o("Menu",[["line",{x1:"4",x2:"20",y1:"12",y2:"12",key:"1e0a9i"}],["line",{x1:"4",x2:"20",y1:"6",y2:"6",key:"1owob3"}],["line",{x1:"4",x2:"20",y1:"18",y2:"18",key:"yk5zj1"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ds=o("Minus",[["path",{d:"M5 12h14",key:"1ays0h"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Us=o("Monitor",[["rect",{width:"20",height:"14",x:"2",y:"3",rx:"2",key:"48i651"}],["line",{x1:"8",x2:"16",y1:"21",y2:"21",key:"1svkeh"}],["line",{x1:"12",x2:"12",y1:"17",y2:"21",key:"vw1qmm"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Gs=o("PackageSearch",[["path",{d:"M21 10V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l2-1.14",key:"e7tb2h"}],["path",{d:"m7.5 4.27 9 5.15",key:"1c824w"}],["polyline",{points:"3.29 7 12 12 20.71 7",key:"ousv84"}],["line",{x1:"12",x2:"12",y1:"22",y2:"12",key:"a4e8g8"}],["circle",{cx:"18.5",cy:"15.5",r:"2.5",key:"b5zd12"}],["path",{d:"M20.27 17.27 22 19",key:"1l4muz"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ot=o("Package",[["path",{d:"M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z",key:"1a0edw"}],["path",{d:"M12 22V12",key:"d0xqtd"}],["path",{d:"m3.3 7 7.703 4.734a2 2 0 0 0 1.994 0L20.7 7",key:"yx3hmr"}],["path",{d:"m7.5 4.27 9 5.15",key:"1c824w"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ks=o("Pause",[["rect",{x:"14",y:"4",width:"4",height:"16",rx:"1",key:"zuxfzm"}],["rect",{x:"6",y:"4",width:"4",height:"16",rx:"1",key:"1okwgv"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const _s=o("Pen",[["path",{d:"M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z",key:"1a8usu"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ws=o("Pencil",[["path",{d:"M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z",key:"1a8usu"}],["path",{d:"m15 5 4 4",key:"1mk7zo"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ft=o("Percent",[["line",{x1:"19",x2:"5",y1:"5",y2:"19",key:"1x9vlm"}],["circle",{cx:"6.5",cy:"6.5",r:"2.5",key:"4mh3h7"}],["circle",{cx:"17.5",cy:"17.5",r:"2.5",key:"1mdrzq"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Qs=o("PinOff",[["path",{d:"M12 17v5",key:"bb1du9"}],["path",{d:"M15 9.34V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H7.89",key:"znwnzq"}],["path",{d:"m2 2 20 20",key:"1ooewy"}],["path",{d:"M9 9v1.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h11",key:"c9qhm2"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Zs=o("Pin",[["path",{d:"M12 17v5",key:"bb1du9"}],["path",{d:"M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z",key:"1nkz8b"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ys=o("Play",[["polygon",{points:"6 3 20 12 6 21 6 3",key:"1oa8hb"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Xs=o("Plug",[["path",{d:"M12 22v-5",key:"1ega77"}],["path",{d:"M9 8V2",key:"14iosj"}],["path",{d:"M15 8V2",key:"18g5xt"}],["path",{d:"M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8Z",key:"osxo6l"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Js=o("Plus",[["path",{d:"M5 12h14",key:"1ays0h"}],["path",{d:"M12 5v14",key:"s699le"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ea=o("Printer",[["path",{d:"M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2",key:"143wyd"}],["path",{d:"M6 9V3a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v6",key:"1itne7"}],["rect",{x:"6",y:"14",width:"12",height:"8",rx:"1",key:"1ue0tg"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ta=o("QrCode",[["rect",{width:"5",height:"5",x:"3",y:"3",rx:"1",key:"1tu5fj"}],["rect",{width:"5",height:"5",x:"16",y:"3",rx:"1",key:"1v8r4q"}],["rect",{width:"5",height:"5",x:"3",y:"16",rx:"1",key:"1x03jg"}],["path",{d:"M21 16h-3a2 2 0 0 0-2 2v3",key:"177gqh"}],["path",{d:"M21 21v.01",key:"ents32"}],["path",{d:"M12 7v3a2 2 0 0 1-2 2H7",key:"8crl2c"}],["path",{d:"M3 12h.01",key:"nlz23k"}],["path",{d:"M12 3h.01",key:"n36tog"}],["path",{d:"M12 16v.01",key:"133mhm"}],["path",{d:"M16 12h1",key:"1slzba"}],["path",{d:"M21 12v.01",key:"1lwtk9"}],["path",{d:"M12 21v-1",key:"1880an"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ra=o("Radio",[["path",{d:"M4.9 19.1C1 15.2 1 8.8 4.9 4.9",key:"1vaf9d"}],["path",{d:"M7.8 16.2c-2.3-2.3-2.3-6.1 0-8.5",key:"u1ii0m"}],["circle",{cx:"12",cy:"12",r:"2",key:"1c9p78"}],["path",{d:"M16.2 7.8c2.3 2.3 2.3 6.1 0 8.5",key:"1j5fej"}],["path",{d:"M19.1 4.9C23 8.8 23 15.1 19.1 19",key:"10b0cb"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Dt=o("Receipt",[["path",{d:"M4 2v20l2-1 2 1 2-1 2 1 2-1 2 1 2-1 2 1V2l-2 1-2-1-2 1-2-1-2 1-2-1-2 1Z",key:"q3az6g"}],["path",{d:"M16 8h-6a2 2 0 1 0 0 4h4a2 2 0 1 1 0 4H8",key:"1h4pet"}],["path",{d:"M12 17.5v-11",key:"1jc1ny"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const sa=o("RefreshCw",[["path",{d:"M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8",key:"v9h5vc"}],["path",{d:"M21 3v5h-5",key:"1q7to0"}],["path",{d:"M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16",key:"3uifl3"}],["path",{d:"M8 16H3v5",key:"1cv678"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const aa=o("RotateCcw",[["path",{d:"M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8",key:"1357e3"}],["path",{d:"M3 3v5h5",key:"1xhq8a"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const oa=o("Save",[["path",{d:"M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z",key:"1c8476"}],["path",{d:"M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7",key:"1ydtos"}],["path",{d:"M7 3v4a1 1 0 0 0 1 1h7",key:"t51u73"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const na=o("ScrollText",[["path",{d:"M15 12h-5",key:"r7krc0"}],["path",{d:"M15 8h-5",key:"1khuty"}],["path",{d:"M19 17V5a2 2 0 0 0-2-2H4",key:"zz82l3"}],["path",{d:"M8 21h12a2 2 0 0 0 2-2v-1a1 1 0 0 0-1-1H11a1 1 0 0 0-1 1v1a2 2 0 1 1-4 0V5a2 2 0 1 0-4 0v2a1 1 0 0 0 1 1h3",key:"1ph1d7"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ia=o("Search",[["circle",{cx:"11",cy:"11",r:"8",key:"4ej97u"}],["path",{d:"m21 21-4.3-4.3",key:"1qie3q"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ca=o("Send",[["path",{d:"M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z",key:"1ffxy3"}],["path",{d:"m21.854 2.147-10.94 10.939",key:"12cjpa"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const la=o("Server",[["rect",{width:"20",height:"8",x:"2",y:"2",rx:"2",ry:"2",key:"ngkwjq"}],["rect",{width:"20",height:"8",x:"2",y:"14",rx:"2",ry:"2",key:"iecqi9"}],["line",{x1:"6",x2:"6.01",y1:"6",y2:"6",key:"16zg32"}],["line",{x1:"6",x2:"6.01",y1:"18",y2:"18",key:"nzw8ys"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const da=o("Settings",[["path",{d:"M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z",key:"1qme2f"}],["circle",{cx:"12",cy:"12",r:"3",key:"1v7zrd"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ua=o("ShieldAlert",[["path",{d:"M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",key:"oel41y"}],["path",{d:"M12 8v4",key:"1got3b"}],["path",{d:"M12 16h.01",key:"1drbdi"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ha=o("ShieldCheck",[["path",{d:"M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",key:"oel41y"}],["path",{d:"m9 12 2 2 4-4",key:"dzmm74"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const pa=o("Shield",[["path",{d:"M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",key:"oel41y"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ya=o("ShoppingBag",[["path",{d:"M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4Z",key:"hou9p0"}],["path",{d:"M3 6h18",key:"d0wm0j"}],["path",{d:"M16 10a4 4 0 0 1-8 0",key:"1ltviw"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ut=o("ShoppingCart",[["circle",{cx:"8",cy:"21",r:"1",key:"jimo8o"}],["circle",{cx:"19",cy:"21",r:"1",key:"13723u"}],["path",{d:"M2.05 2.05h2l2.66 12.42a2 2 0 0 0 2 1.58h9.78a2 2 0 0 0 1.95-1.57l1.65-7.43H5.12",key:"9zh506"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ma=o("SlidersHorizontal",[["line",{x1:"21",x2:"14",y1:"4",y2:"4",key:"obuewd"}],["line",{x1:"10",x2:"3",y1:"4",y2:"4",key:"1q6298"}],["line",{x1:"21",x2:"12",y1:"12",y2:"12",key:"1iu8h1"}],["line",{x1:"8",x2:"3",y1:"12",y2:"12",key:"ntss68"}],["line",{x1:"21",x2:"16",y1:"20",y2:"20",key:"14d8ph"}],["line",{x1:"12",x2:"3",y1:"20",y2:"20",key:"m0wm8r"}],["line",{x1:"14",x2:"14",y1:"2",y2:"6",key:"14e1ph"}],["line",{x1:"8",x2:"8",y1:"10",y2:"14",key:"1i6ji0"}],["line",{x1:"16",x2:"16",y1:"18",y2:"22",key:"1lctlv"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ga=o("Sparkles",[["path",{d:"M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5l1.582-6.135a.5.5 0 0 1 .963 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.581a.5.5 0 0 1 0 .964L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.963 0z",key:"4pj2yx"}],["path",{d:"M20 3v4",key:"1olli1"}],["path",{d:"M22 5h-4",key:"1gvqau"}],["path",{d:"M4 17v2",key:"vumght"}],["path",{d:"M5 18H3",key:"zchphs"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const xa=o("Star",[["path",{d:"M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z",key:"r04s7s"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const fa=o("StickyNote",[["path",{d:"M16 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V8Z",key:"qazsjp"}],["path",{d:"M15 3v4a2 2 0 0 0 2 2h4",key:"40519r"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ka=o("Store",[["path",{d:"m2 7 4.41-4.41A2 2 0 0 1 7.83 2h8.34a2 2 0 0 1 1.42.59L22 7",key:"ztvudi"}],["path",{d:"M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8",key:"1b2hhj"}],["path",{d:"M15 22v-4a2 2 0 0 0-2-2h-2a2 2 0 0 0-2 2v4",key:"2ebpfo"}],["path",{d:"M2 7h20",key:"1fcdvo"}],["path",{d:"M22 7v3a2 2 0 0 1-2 2a2.7 2.7 0 0 1-1.59-.63.7.7 0 0 0-.82 0A2.7 2.7 0 0 1 16 12a2.7 2.7 0 0 1-1.59-.63.7.7 0 0 0-.82 0A2.7 2.7 0 0 1 12 12a2.7 2.7 0 0 1-1.59-.63.7.7 0 0 0-.82 0A2.7 2.7 0 0 1 8 12a2.7 2.7 0 0 1-1.59-.63.7.7 0 0 0-.82 0A2.7 2.7 0 0 1 4 12a2 2 0 0 1-2-2V7",key:"6c3vgh"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ba=o("Tag",[["path",{d:"M12.586 2.586A2 2 0 0 0 11.172 2H4a2 2 0 0 0-2 2v7.172a2 2 0 0 0 .586 1.414l8.704 8.704a2.426 2.426 0 0 0 3.42 0l6.58-6.58a2.426 2.426 0 0 0 0-3.42z",key:"vktsd0"}],["circle",{cx:"7.5",cy:"7.5",r:".5",fill:"currentColor",key:"kqv944"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const va=o("Tags",[["path",{d:"m15 5 6.3 6.3a2.4 2.4 0 0 1 0 3.4L17 19",key:"1cbfv1"}],["path",{d:"M9.586 5.586A2 2 0 0 0 8.172 5H3a1 1 0 0 0-1 1v5.172a2 2 0 0 0 .586 1.414L8.29 18.29a2.426 2.426 0 0 0 3.42 0l3.58-3.58a2.426 2.426 0 0 0 0-3.42z",key:"135mg7"}],["circle",{cx:"6.5",cy:"9.5",r:".5",fill:"currentColor",key:"5pm5xn"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const wa=o("Target",[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["circle",{cx:"12",cy:"12",r:"6",key:"1vlfrh"}],["circle",{cx:"12",cy:"12",r:"2",key:"1c9p78"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ja=o("Trash2",[["path",{d:"M3 6h18",key:"d0wm0j"}],["path",{d:"M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6",key:"4alrt4"}],["path",{d:"M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2",key:"v07s0e"}],["line",{x1:"10",x2:"10",y1:"11",y2:"17",key:"1uufr5"}],["line",{x1:"14",x2:"14",y1:"11",y2:"17",key:"xtxkd"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ma=o("TrendingDown",[["polyline",{points:"22 17 13.5 8.5 8.5 13.5 2 7",key:"1r2t7k"}],["polyline",{points:"16 17 22 17 22 11",key:"11uiuu"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Gt=o("TrendingUp",[["polyline",{points:"22 7 13.5 15.5 8.5 10.5 2 17",key:"126l90"}],["polyline",{points:"16 7 22 7 22 13",key:"kwv8wd"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ve=o("TriangleAlert",[["path",{d:"m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3",key:"wmoenq"}],["path",{d:"M12 9v4",key:"juzpu7"}],["path",{d:"M12 17h.01",key:"p32p05"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ca=o("Truck",[["path",{d:"M14 18V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v11a1 1 0 0 0 1 1h2",key:"wrbu53"}],["path",{d:"M15 18H9",key:"1lyqi6"}],["path",{d:"M19 18h2a1 1 0 0 0 1-1v-3.65a1 1 0 0 0-.22-.624l-3.48-4.35A1 1 0 0 0 17.52 8H14",key:"lysw3i"}],["circle",{cx:"17",cy:"18",r:"2",key:"332jqn"}],["circle",{cx:"7",cy:"18",r:"2",key:"19iecd"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Sa=o("UserCog",[["circle",{cx:"18",cy:"15",r:"3",key:"gjjjvw"}],["circle",{cx:"9",cy:"7",r:"4",key:"nufk8"}],["path",{d:"M10 15H6a4 4 0 0 0-4 4v2",key:"1nfge6"}],["path",{d:"m21.7 16.4-.9-.3",key:"12j9ji"}],["path",{d:"m15.2 13.9-.9-.3",key:"1fdjdi"}],["path",{d:"m16.6 18.7.3-.9",key:"heedtr"}],["path",{d:"m19.1 12.2.3-.9",key:"1af3ki"}],["path",{d:"m19.6 18.7-.4-1",key:"1x9vze"}],["path",{d:"m16.8 12.3-.4-1",key:"vqeiwj"}],["path",{d:"m14.3 16.6 1-.4",key:"1qlj63"}],["path",{d:"m20.7 13.8 1-.4",key:"1v5t8k"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Na=o("UserPlus",[["path",{d:"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2",key:"1yyitq"}],["circle",{cx:"9",cy:"7",r:"4",key:"nufk8"}],["line",{x1:"19",x2:"19",y1:"8",y2:"14",key:"1bvyxn"}],["line",{x1:"22",x2:"16",y1:"11",y2:"11",key:"1shjgl"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const za=o("User",[["path",{d:"M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2",key:"975kel"}],["circle",{cx:"12",cy:"7",r:"4",key:"17ys0d"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Aa=o("Users",[["path",{d:"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2",key:"1yyitq"}],["circle",{cx:"9",cy:"7",r:"4",key:"nufk8"}],["path",{d:"M22 21v-2a4 4 0 0 0-3-3.87",key:"kshegd"}],["path",{d:"M16 3.13a4 4 0 0 1 0 7.75",key:"1da9ce"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Kt=o("UtensilsCrossed",[["path",{d:"m16 2-2.3 2.3a3 3 0 0 0 0 4.2l1.8 1.8a3 3 0 0 0 4.2 0L22 8",key:"n7qcjb"}],["path",{d:"M15 15 3.3 3.3a4.2 4.2 0 0 0 0 6l7.3 7.3c.7.7 2 .7 2.8 0L15 15Zm0 0 7 7",key:"d0u48b"}],["path",{d:"m2.1 21.8 6.4-6.3",key:"yn04lh"}],["path",{d:"m19 5-7 7",key:"194lzd"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const qa=o("Wallet",[["path",{d:"M19 7V4a1 1 0 0 0-1-1H5a2 2 0 0 0 0 4h15a1 1 0 0 1 1 1v4h-3a2 2 0 0 0 0 4h3a1 1 0 0 0 1-1v-2a1 1 0 0 0-1-1",key:"18etb6"}],["path",{d:"M3 5v14a2 2 0 0 0 2 2h15a1 1 0 0 0 1-1v-4",key:"xoc0q4"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const La=o("Warehouse",[["path",{d:"M22 8.35V20a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8.35A2 2 0 0 1 3.26 6.5l8-3.2a2 2 0 0 1 1.48 0l8 3.2A2 2 0 0 1 22 8.35Z",key:"gksnxg"}],["path",{d:"M6 18h12",key:"9pbo8z"}],["path",{d:"M6 14h12",key:"4cwo0f"}],["rect",{width:"12",height:"12",x:"6",y:"10",key:"apd30q"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Pa=o("Wifi",[["path",{d:"M12 20h.01",key:"zekei9"}],["path",{d:"M2 8.82a15 15 0 0 1 20 0",key:"dnpr2z"}],["path",{d:"M5 12.859a10 10 0 0 1 14 0",key:"1x1e6c"}],["path",{d:"M8.5 16.429a5 5 0 0 1 7 0",key:"1bycff"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const _t=o("X",[["path",{d:"M18 6 6 18",key:"1bl5f8"}],["path",{d:"m6 6 12 12",key:"d8bk6v"}]]);/**
 * @license lucide-react v0.462.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ta=o("Zap",[["path",{d:"M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",key:"1xq2db"}]]);function O({className:e,...t}){return s.jsx("div",{className:_("animate-pulse rounded-md bg-muted",e),...t})}function Wt({title:e,description:t,actions:r,className:a,eyebrow:i}){return s.jsxs("div",{className:_("flex items-start justify-between gap-4",a),children:[s.jsxs("div",{className:"flex items-start gap-3 min-w-0",children:[s.jsx("div",{className:"w-[3px] rounded-full bg-primary self-stretch min-h-[24px] shrink-0 mt-0.5"}),s.jsxs("div",{className:"min-w-0",children:[i&&s.jsx("p",{className:"text-[10px] font-semibold uppercase tracking-[0.12em] text-primary mb-1",children:i}),s.jsx("h1",{className:"font-display text-[20px] font-bold text-foreground leading-tight tracking-tight",children:e}),t&&s.jsx("p",{className:"text-[12.5px] text-muted-foreground mt-1 leading-snug",children:t})]})]}),r&&s.jsx("div",{className:"flex items-center gap-2 shrink-0 pt-0.5",children:r})]})}function J(e){return e.toLocaleString("pt-BR",{style:"currency",currency:"BRL"})}function Ia(e){return new Date(e).toLocaleString("pt-BR",{day:"2-digit",month:"2-digit",year:"numeric"})}function Ra(e){return new Date(e).toLocaleString("pt-BR",{day:"2-digit",month:"2-digit",year:"numeric",hour:"2-digit",minute:"2-digit"})}function Qt(){return f.get("/dashboard/summary")}const Zt=["dashboard","summary"];function V(){return R({queryKey:Zt,queryFn:Qt,staleTime:6e4})}const Yt={indigo:"bg-[#5B4DFF]",success:"bg-success",secondary:"bg-secondary",warning:"bg-warning"},Xt={indigo:"text-[#5B4DFF]",success:"text-success",secondary:"text-secondary",warning:"text-warning"};function Jt({kpi:e}){return s.jsxs("div",{className:"bg-card rounded-xl border border-border p-5 animate-fade-in relative overflow-hidden",children:[s.jsx("div",{className:_("absolute top-0 left-0 right-0 h-[2px]",Yt[e.accent])}),s.jsxs("div",{className:"flex items-center justify-between mb-3 pt-0.5",children:[s.jsx("p",{className:"text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground",children:e.label}),s.jsx(e.icon,{className:_("h-3.5 w-3.5 shrink-0",Xt[e.accent])})]}),s.jsx("p",{className:"text-[26px] font-bold text-foreground leading-none tracking-tight font-display",children:e.value}),s.jsx("p",{className:_("text-[11px] mt-2 font-medium",e.subOk?"text-muted-foreground":"text-warning"),children:e.sub})]})}function er(){const{data:e,isLoading:t}=V();if(t)return s.jsx("div",{className:"grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4",children:Array.from({length:4}).map((i,n)=>s.jsx(O,{className:"h-[106px] rounded-xl"},n))});const r=((e==null?void 0:e.zeroStockCount)??0)+((e==null?void 0:e.lowStockCount)??0),a=[{label:"Faturamento",value:J((e==null?void 0:e.totalRevenue)??0),sub:`${(e==null?void 0:e.totalSales)??0} venda(s) no período`,subOk:!0,icon:Et,accent:"indigo"},{label:"Ticket médio",value:J((e==null?void 0:e.averageTicket)??0),sub:"por venda ativa",subOk:!0,icon:Gt,accent:"success"},{label:"Vendas",value:String((e==null?void 0:e.totalSales)??0),sub:((e==null?void 0:e.cancelledCount)??0)>0?`${e.cancelledCount} cancelada(s)`:"sem cancelamentos",subOk:((e==null?void 0:e.cancelledCount)??0)===0,icon:Dt,accent:"secondary"},{label:"Alerta estoque",value:String(r),sub:r>0?"itens requerem atenção":"estoque normalizado",subOk:r===0,icon:Ve,accent:r>0?"warning":"success"}];return s.jsx("div",{className:"grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4",children:a.map(i=>s.jsx(Jt,{kpi:i},i.label))})}const tr=["7d","30d"];function rr(){const[e,t]=b.useState("7d"),{data:r,isLoading:a}=V(),i=b.useMemo(()=>{const n=(r==null?void 0:r.salesByDay)??[],l=e==="7d"?new Date(Date.now()-7*24*60*60*1e3).toISOString().split("T")[0]:null;return n.filter(c=>l===null||c.date>=l).map(c=>{const d=new Date(c.date+"T12:00:00");return{name:`${String(d.getDate()).padStart(2,"0")}/${String(d.getMonth()+1).padStart(2,"0")}`,vendas:c.revenue}})},[r==null?void 0:r.salesByDay,e]);return s.jsxs("div",{className:"bg-card rounded-xl border border-border p-5 animate-fade-in",children:[s.jsxs("div",{className:"flex items-center justify-between mb-5",children:[s.jsxs("div",{children:[s.jsx("h3",{className:"text-sm font-semibold text-foreground",children:"Vendas"}),s.jsx("p",{className:"text-xs text-muted-foreground",children:"Receita por data"})]}),s.jsx("div",{className:"flex gap-1",children:tr.map(n=>s.jsx("button",{onClick:()=>t(n),className:`px-3 py-1 rounded-md text-xs font-medium transition-colors ${e===n?"bg-primary text-primary-foreground":"text-muted-foreground hover:bg-muted"}`,children:n},n))})]}),a?s.jsx(O,{className:"h-[260px] w-full"}):i.length===0?s.jsxs("div",{className:"h-[260px] flex flex-col items-center justify-center gap-2",children:[s.jsx("p",{className:"text-sm font-medium text-foreground",children:"Nenhuma venda ainda."}),s.jsx("p",{className:"text-xs text-muted-foreground",children:"Abra o caixa e registre a primeira venda pelo PDV."})]}):s.jsx(Ee,{width:"100%",height:260,children:s.jsxs($e,{data:i,children:[s.jsx("defs",{children:s.jsxs("linearGradient",{id:"salesGradient",x1:"0",y1:"0",x2:"0",y2:"1",children:[s.jsx("stop",{offset:"5%",stopColor:"hsl(217, 91%, 60%)",stopOpacity:.2}),s.jsx("stop",{offset:"95%",stopColor:"hsl(217, 91%, 60%)",stopOpacity:0})]})}),s.jsx(Be,{strokeDasharray:"3 3",stroke:"hsl(214, 32%, 91%)",vertical:!1}),s.jsx(Oe,{dataKey:"name",axisLine:!1,tickLine:!1,tick:{fontSize:11,fill:"hsl(215, 16%, 47%)"}}),s.jsx(Fe,{axisLine:!1,tickLine:!1,tick:{fontSize:11,fill:"hsl(215, 16%, 47%)"},tickFormatter:n=>n>=1e3?`${(n/1e3).toFixed(0)}k`:String(n)}),s.jsx(De,{contentStyle:{backgroundColor:"hsl(222, 47%, 11%)",border:"none",borderRadius:8,fontSize:12,color:"#fff"},formatter:n=>[n.toLocaleString("pt-BR",{style:"currency",currency:"BRL"}),"Receita"]}),s.jsx(Ue,{type:"monotone",dataKey:"vendas",stroke:"hsl(217, 91%, 60%)",strokeWidth:2,fill:"url(#salesGradient)"})]})})]})}function sr(){const{data:e,isLoading:t}=V(),r=(e==null?void 0:e.topProducts)??[];return s.jsxs("div",{className:"bg-card rounded-xl border border-border p-5 animate-fade-in",children:[s.jsxs("div",{className:"flex items-center justify-between mb-4",children:[s.jsx("h3",{className:"text-sm font-semibold text-foreground",children:"Produtos mais vendidos"}),r.length>0&&s.jsx(ee,{to:"/relatorios",className:"text-[11px] text-muted-foreground hover:text-foreground transition-colors",children:"Ver relatório"})]}),t?s.jsx("div",{className:"space-y-3",children:Array.from({length:5}).map((a,i)=>s.jsx(O,{className:"h-8 w-full"},i))}):r.length===0?s.jsxs("div",{className:"py-4 text-center space-y-1",children:[s.jsx("p",{className:"text-sm font-medium text-foreground",children:"Nenhuma venda ainda."}),s.jsx("p",{className:"text-xs text-muted-foreground",children:"Os produtos mais vendidos aparecem aqui."})]}):s.jsx("div",{className:"space-y-3",children:r.map((a,i)=>s.jsxs("div",{className:"flex items-center gap-3",children:[s.jsx("span",{className:"text-xs font-bold text-muted-foreground w-5 text-right shrink-0",children:i+1}),s.jsxs("div",{className:"flex-1 min-w-0",children:[s.jsx("p",{className:"text-sm font-medium text-foreground truncate",children:a.productName}),s.jsxs("p",{className:"text-xs text-muted-foreground",children:[a.quantitySold," un."]})]}),s.jsx("span",{className:"text-sm font-semibold text-foreground whitespace-nowrap",children:J(a.revenue)})]},a.productId))})]})}function ar(e){return e.split(" ").slice(0,2).map(t=>{var r;return((r=t[0])==null?void 0:r.toUpperCase())??""}).join("")}function or(){const{data:e,isLoading:t}=V(),r=(e==null?void 0:e.topSellers)??[];return s.jsxs("div",{className:"bg-card rounded-xl border border-border p-5 animate-fade-in",children:[s.jsx("h3",{className:"text-sm font-semibold text-foreground mb-4",children:"Ranking de vendedores"}),t?s.jsx("div",{className:"space-y-3",children:Array.from({length:4}).map((a,i)=>s.jsx(O,{className:"h-8 w-full"},i))}):r.length===0?s.jsxs("div",{className:"py-4 text-center space-y-1",children:[s.jsx("p",{className:"text-sm font-medium text-foreground",children:"Sem dados ainda."}),s.jsx("p",{className:"text-xs text-muted-foreground",children:"O ranking aparece após as primeiras vendas."})]}):s.jsx("div",{className:"space-y-3",children:r.map((a,i)=>s.jsxs("div",{className:"flex items-center gap-3",children:[s.jsx("div",{className:"w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center shrink-0",children:s.jsx("span",{className:"text-[10px] font-bold text-primary",children:ar(a.sellerName)})}),s.jsxs("div",{className:"flex-1 min-w-0",children:[s.jsx("p",{className:"text-sm font-medium text-foreground truncate",children:a.sellerName}),s.jsxs("p",{className:"text-xs text-muted-foreground",children:[a.salesCount," venda(s)"]})]}),s.jsx("span",{className:"text-sm font-semibold text-foreground whitespace-nowrap",children:J(a.revenue)}),i===0&&s.jsx("span",{className:"text-[10px] font-bold bg-warning/10 text-warning px-1.5 py-0.5 rounded",children:"🏆"})]},a.sellerName))})]})}const nr={inventory:Ot,cash:$t,sales:Ut,commissions:Ft,operations:Tt},ir={critical:{color:"text-destructive",bg:"bg-destructive/10"},warning:{color:"text-warning",bg:"bg-warning/10"},info:{color:"text-secondary",bg:"bg-secondary/10"}};function cr(){const{data:e,isLoading:t}=V(),r=b.useMemo(()=>{if(!e)return[];const i=[];let n=0;const l=(c,d,h,m,g)=>({id:`i-${n++}`,category:c,severity:d,title:h,description:m,value:g});if(e.zeroStockCount>0&&i.push(l("inventory","critical","Produtos sem estoque","Existem produtos com estoque zerado que podem impedir vendas.",`${e.zeroStockCount} produto${e.zeroStockCount>1?"s":""}`)),e.lowStockCount>0&&i.push(l("inventory","warning","Estoque baixo","Alguns produtos estão com estoque abaixo do nível mínimo.",`${e.lowStockCount} produto${e.lowStockCount>1?"s":""}`)),e.hasOpenCashSession||i.push(l("cash","warning","Caixa não aberto","Não há sessão de caixa ativa. O PDV não poderá finalizar vendas.")),e.totalSales>=5){const c=e.cancelledCount/e.totalSales;c>.1&&i.push(l("sales","warning","Taxa de cancelamento elevada","A proporção de vendas canceladas está acima de 10% do total.",`${Math.round(c*100)}% de cancelamentos`))}if(e.topSellers.length>0){const c=e.topSellers[0];i.push(l("sales","info","Melhor vendedor",`${c.sellerName} lidera em faturamento no período.`,c.revenue.toLocaleString("pt-BR",{style:"currency",currency:"BRL"})))}return i},[e]),a=[...r].sort((i,n)=>{const l={critical:0,warning:1,info:2};return l[i.severity]-l[n.severity]}).slice(0,3);return s.jsxs("div",{className:"bg-card rounded-xl border border-border p-5 animate-fade-in",children:[s.jsxs("div",{className:"flex items-center justify-between mb-4",children:[s.jsx("h3",{className:"text-sm font-semibold text-foreground",children:"Insights recentes"}),r.length>0&&s.jsx(ee,{to:"/insights",className:"text-[11px] text-muted-foreground hover:text-foreground transition-colors",children:"Ver todos"})]}),t?s.jsx("div",{className:"space-y-3",children:Array.from({length:3}).map((i,n)=>s.jsx(O,{className:"h-12 w-full"},n))}):a.length===0?s.jsxs("div",{className:"flex flex-col items-center gap-2 py-4",children:[s.jsx(Bt,{className:"h-5 w-5 text-muted-foreground/50"}),s.jsx("p",{className:"text-sm font-medium text-foreground",children:"Tudo em ordem."}),s.jsx("p",{className:"text-xs text-muted-foreground",children:"Insights aparecem conforme o sistema acumula dados."})]}):s.jsx("div",{className:"space-y-4",children:a.map(i=>{const n=nr[i.category],{color:l,bg:c}=ir[i.severity];return s.jsxs("div",{className:"flex gap-3",children:[s.jsx("div",{className:`w-8 h-8 rounded-lg ${c} flex items-center justify-center shrink-0`,children:s.jsx(n,{className:`h-4 w-4 ${l}`})}),s.jsxs("div",{className:"flex-1 min-w-0",children:[s.jsx("p",{className:"text-sm font-medium text-foreground leading-snug",children:i.title}),s.jsx("p",{className:"text-xs text-muted-foreground mt-0.5 leading-relaxed line-clamp-2",children:i.description}),i.value&&s.jsx("p",{className:"text-[10px] text-muted-foreground mt-1 font-semibold",children:i.value})]})]},i.id)})})]})}function lr(){const{data:e,isLoading:t}=V(),r=(e==null?void 0:e.stockAlerts)??[],a=r.length>0;return s.jsxs("div",{className:"bg-card rounded-xl border border-border p-5 animate-fade-in",children:[s.jsxs("div",{className:"flex items-center justify-between mb-4",children:[s.jsxs("div",{className:"flex items-center gap-2",children:[s.jsx(Ve,{className:"h-4 w-4 text-warning"}),s.jsx("h3",{className:"text-sm font-semibold text-foreground",children:"Alertas de estoque"})]}),a&&s.jsx(ee,{to:"/estoque",className:"text-[11px] text-muted-foreground hover:text-foreground transition-colors",children:"Ver todos"})]}),t?s.jsx("div",{className:"space-y-3",children:Array.from({length:3}).map((i,n)=>s.jsx(O,{className:"h-8 w-full"},n))}):a?s.jsx("div",{className:"space-y-3",children:r.map(i=>{const n=i.minStock>0?Math.round(i.currentStock/i.minStock*100):0,l=i.status==="zero"?"hsl(0, 84%, 60%)":n<=50?"hsl(38, 92%, 50%)":"hsl(160, 84%, 39%)";return s.jsxs("div",{children:[s.jsxs("div",{className:"flex items-center justify-between mb-1",children:[s.jsx("p",{className:"text-sm font-medium text-foreground truncate",children:i.productName}),s.jsxs("span",{className:"text-xs font-semibold text-destructive whitespace-nowrap ml-2",children:[i.currentStock,"/",i.minStock]})]}),s.jsx("div",{className:"w-full h-1.5 bg-muted rounded-full overflow-hidden",children:s.jsx("div",{className:"h-full rounded-full transition-all",style:{width:`${Math.min(n,100)}%`,backgroundColor:l}})})]},i.productId)})}):s.jsxs("div",{className:"py-4 text-center space-y-1",children:[s.jsx("p",{className:"text-sm font-medium text-foreground",children:"Estoque saudável."}),s.jsx("p",{className:"text-xs text-muted-foreground",children:"Nenhum produto abaixo do mínimo."})]})]})}function dr(e=!1,t){const r=new URLSearchParams;e&&r.set("includeInactive","true"),t!==void 0&&r.set("isIngredient",String(t));const a=r.toString();return f.get(`/products${a?`?${a}`:""}`)}function ur(e={}){const{page:t=1,pageSize:r=50,search:a,includeInactive:i=!1,isIngredient:n,categoryId:l,unit:c}=e,d=new URLSearchParams({page:String(t),pageSize:String(r)});return i&&d.set("includeInactive","true"),n!==void 0&&d.set("isIngredient",String(n)),a&&d.set("search",a),l&&d.set("categoryId",l),c&&d.set("unit",c),f.get(`/products/paged?${d}`)}function hr(e){return f.get(`/products/${e}`)}function pr(e){return f.post("/products",e)}function yr(e,t){return f.put(`/products/${e}`,t)}function mr(e){return f.post(`/products/${e}/activate`)}function gr(e){return f.post(`/products/${e}/deactivate`)}function xr(){return f.get("/categories")}function fr(e){return f.post("/categories",e)}function kr(e,t){return f.put(`/categories/${e}`,t)}function br(e){return f.delete(`/categories/${e}`)}const A=["products"],ae=["categories"];function vr(e){const{includeInactive:t=!1,isIngredient:r}=e??{};return R({queryKey:[...A,{includeInactive:t,isIngredient:r}],queryFn:()=>dr(t,r)})}function Va(e){return R({queryKey:[...A,e],queryFn:()=>hr(e),enabled:!!e})}function Ha(e){return R({queryKey:[...A,"paged",e],queryFn:()=>ur(e),staleTime:3e4,placeholderData:t=>t})}function Ea(){return R({queryKey:ae,queryFn:xr,staleTime:5*6e4})}function $a(){const e=q();return L({mutationFn:t=>pr(t),onSuccess:()=>{e.invalidateQueries({queryKey:A})}})}function Ba(e){const t=q();return L({mutationFn:r=>yr(e,r),onSuccess:r=>{t.setQueryData([...A,e],r),t.invalidateQueries({queryKey:A})}})}function Oa(e){const t=q();return L({mutationFn:r=>r?mr(e):gr(e),onSuccess:()=>{t.invalidateQueries({queryKey:[...A,e]}),t.invalidateQueries({queryKey:A})}})}function Fa(){const e=q();return L({mutationFn:t=>fr(t),onSuccess:()=>e.invalidateQueries({queryKey:ae})})}function Da(e){const t=q();return L({mutationFn:r=>kr(e,r),onSuccess:()=>t.invalidateQueries({queryKey:ae})})}function Ua(){const e=q();return L({mutationFn:t=>br(t),onSuccess:()=>{e.invalidateQueries({queryKey:ae}),e.invalidateQueries({queryKey:A})}})}function wr(){return f.get("/cash/sessions/open")}function jr(e){return f.get(`/cash/sessions/${e}`)}function Mr(e){return f.post("/cash/sessions/open",e)}function Cr(e,t){return f.post(`/cash/sessions/${e}/close`,t)}function Sr(e,t){return f.post(`/cash/sessions/${e}/movements`,t)}const W=["cash"];function Nr(){return R({queryKey:[...W,"open"],queryFn:wr})}function Ga(e){return R({queryKey:[...W,"session",e],queryFn:()=>jr(e),enabled:!!e})}function Ka(){const e=q();return L({mutationFn:t=>Mr(t),onSuccess:()=>{e.invalidateQueries({queryKey:W})}})}function _a(){const e=q();return L({mutationFn:({id:t,req:r})=>Cr(t,r),onSuccess:()=>{e.invalidateQueries({queryKey:W})}})}function Wa(){const e=q();return L({mutationFn:({id:t,req:r})=>Sr(t,r),onSuccess:()=>{e.invalidateQueries({queryKey:W})}})}function zr({onDismiss:e}){const{data:t=[],isLoading:r}=vr(),{data:a,isLoading:i}=Nr(),{data:n,isLoading:l}=V(),c=r||i||l,d=t.length>0,h=(a==null?void 0:a.status)==="Open"||((n==null?void 0:n.totalSales)??0)>0,m=((n==null?void 0:n.totalSales)??0)>0,g=d&&h&&m;if(b.useEffect(()=>{!c&&g&&e()},[c,g,e]),!c&&g)return null;const p=[{id:"product",label:"Cadastre um produto",hint:"Adicione pelo menos um item para vender",href:"/produtos/novo",done:d},{id:"cash",label:"Abra o caixa",hint:"Ative uma sessão de caixa antes de vender",href:"/caixa",done:h},{id:"sale",label:"Faça a primeira venda",hint:"Registre uma venda pelo PDV",href:"/pdv",done:m}],M=p.filter(v=>v.done).length;return s.jsxs("div",{className:"bg-card border border-border rounded-xl p-5 animate-fade-in",children:[s.jsxs("div",{className:"flex items-start justify-between gap-4 mb-4",children:[s.jsxs("div",{children:[s.jsx("p",{className:"text-[13px] font-semibold text-foreground",children:"Configure o Orken"}),s.jsxs("p",{className:"text-[12px] text-muted-foreground mt-0.5",children:[M," de ",p.length," etapas concluídas"]})]}),s.jsx("button",{type:"button",onClick:e,className:"text-muted-foreground hover:text-foreground transition-colors mt-0.5 shrink-0","aria-label":"Fechar guia de configuração",children:s.jsx(_t,{className:"h-4 w-4"})})]}),s.jsx("div",{className:"w-full h-1 bg-muted rounded-full mb-4 overflow-hidden",children:s.jsx("div",{className:"h-full bg-primary rounded-full transition-all duration-500",style:{width:`${M/p.length*100}%`}})}),s.jsx("div",{className:"flex flex-col sm:flex-row gap-2",children:p.map(v=>v.done?s.jsxs("div",{className:"flex items-center gap-2 flex-1 px-3 py-2.5 rounded-md bg-success/5 border border-success/20",children:[s.jsx(Vt,{className:"h-4 w-4 text-success shrink-0"}),s.jsx("div",{className:"min-w-0",children:s.jsx("p",{className:"text-xs font-medium text-foreground line-through text-muted-foreground",children:v.label})})]},v.id):s.jsxs(ee,{to:v.href,className:"flex items-center gap-2 flex-1 px-3 py-2.5 rounded-md bg-muted/50 hover:bg-muted border border-border hover:border-primary/30 transition-colors group",children:[s.jsx(Ht,{className:"h-4 w-4 text-muted-foreground shrink-0 group-hover:text-primary transition-colors"}),s.jsxs("div",{className:"flex-1 min-w-0",children:[s.jsx("p",{className:"text-xs font-medium text-foreground",children:v.label}),s.jsx("p",{className:"text-[10px] text-muted-foreground leading-relaxed hidden sm:block",children:v.hint})]}),s.jsx(Rt,{className:"h-3.5 w-3.5 text-muted-foreground group-hover:text-primary transition-colors shrink-0"})]},v.id))})]})}function Ar(){const{session:e}=Ie(),t=(e==null?void 0:e.storeId)??"",{data:r,isLoading:a}=Ge(t),{data:i,isLoading:n}=Ke(t),l=(r==null?void 0:r.filter(d=>d.status==="Occupied").length)??0,c=(i==null?void 0:i.filter(d=>d.status!=="Delivered").length)??0;return s.jsxs("div",{className:"grid grid-cols-1 sm:grid-cols-2 gap-4",children:[s.jsxs("div",{className:"rounded-xl border border-border bg-card p-5 relative overflow-hidden",children:[s.jsx("div",{className:"absolute top-0 left-0 right-0 h-[2px] bg-[#5B4DFF]"}),s.jsxs("div",{className:"flex items-center justify-between mb-3 pt-0.5",children:[s.jsx("p",{className:"text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground",children:"Mesas abertas"}),s.jsx(Kt,{className:"h-3.5 w-3.5 text-[#5B4DFF]"})]}),a?s.jsx("div",{className:"h-8 w-16 bg-muted animate-pulse rounded"}):s.jsx("p",{className:"font-display text-[26px] font-bold text-foreground leading-none",children:l===0?"—":l}),s.jsx("p",{className:"text-[11px] mt-2 text-muted-foreground font-medium",children:a?"":l===0?"Nenhuma mesa ocupada":"mesas em atendimento"})]}),s.jsxs("div",{className:"rounded-xl border border-border bg-card p-5 relative overflow-hidden",children:[s.jsx("div",{className:`absolute top-0 left-0 right-0 h-[2px] ${c>0?"bg-warning":"bg-success"}`}),s.jsxs("div",{className:"flex items-center justify-between mb-3 pt-0.5",children:[s.jsx("p",{className:"text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground",children:"Cozinha"}),s.jsx(It,{className:`h-3.5 w-3.5 ${c>0?"text-warning":"text-success"}`})]}),n?s.jsx("div",{className:"h-8 w-16 bg-muted animate-pulse rounded"}):s.jsx("p",{className:"font-display text-[26px] font-bold text-foreground leading-none",children:c===0?"—":c}),s.jsx("p",{className:"text-[11px] mt-2 text-muted-foreground font-medium",children:n?"":c===0?"Tudo em ordem":"pedidos em preparo"})]})]})}function qr(e){const t=e?`nexo:setup-dismissed:${e}`:null,[r,a]=b.useState(()=>!!t&&localStorage.getItem(t)==="1");function i(){t&&localStorage.setItem(t,"1"),a(!0)}return{dismissed:r,dismiss:i}}function Lr(){const e=new Date().getHours();return e<12?"Bom dia":e<18?"Boa tarde":"Boa noite"}function Pr(){var n;const{session:e}=Ie(),{dismissed:t,dismiss:r}=qr(e==null?void 0:e.userId),a=b.useMemo(()=>Lr(),[]),i=((n=e==null?void 0:e.name)==null?void 0:n.split(" ")[0])??"";return s.jsxs("div",{className:"space-y-6",children:[!t&&s.jsx(zr,{onDismiss:r}),s.jsx(Wt,{title:i?`${a}, ${i}`:a,description:"Visão geral da operação hoje"}),s.jsx(er,{}),(e==null?void 0:e.modules.includes("restaurante"))&&s.jsx(Ar,{}),s.jsxs("div",{className:"grid grid-cols-1 lg:grid-cols-3 gap-6",children:[s.jsx("div",{className:"lg:col-span-2",children:s.jsx(rr,{})}),s.jsx(sr,{})]}),s.jsxs("div",{className:"grid grid-cols-1 lg:grid-cols-3 gap-6",children:[s.jsx(or,{}),s.jsx(cr,{}),s.jsx(lr,{})]})]})}const Qa=Object.freeze(Object.defineProperty({__proto__:null,default:Pr},Symbol.toStringTag,{value:"Module"}));export{Ms as $,Tt as A,Jr as B,as as C,js as D,gs as E,vs as F,ca as G,zs as H,hs as I,Ns as J,Ts as K,$s as L,Fs as M,ua as N,is as O,Ot as P,xa as Q,sa as R,ka as S,Gt as T,Aa as U,Is as V,Pa as W,_t as X,xs as Y,Ta as Z,fs as _,_e as a,Wr as a$,Et as a0,ks as a1,Es as a2,Os as a3,ba as a4,Ut as a5,Ca as a6,us as a7,Ht as a8,Ie as a9,It as aA,ma as aB,rs as aC,Ss as aD,Sa as aE,pa as aF,da as aG,Qr as aH,ha as aI,na as aJ,Xs as aK,ra as aL,je as aM,Hr as aN,Bs as aO,cs as aP,$r as aQ,Er as aR,Br as aS,te as aT,S as aU,Ra as aV,Ia as aW,O as aX,J as aY,es as aZ,za as a_,ds as aa,qe as ab,vr as ac,Ea as ad,ea as ae,Ws as af,ya as ag,ls as ah,Dt as ai,Wt as aj,wa as ak,Bt as al,Ur as am,Kr as an,Fr as ao,As as ap,qs as aq,Cs as ar,ps as as,Vs as at,_r as au,Rs as av,qa as aw,La as ax,Kt as ay,Zr as az,f as b,ta as b0,ys as b1,Fa as b2,Ua as b3,Da as b4,Ha as b5,va as b6,Va as b7,$a as b8,Ba as b9,Oa as ba,ms as bb,Ps as bc,Gs as bd,Na as be,Hs as bf,Nr as bg,Ga as bh,Ka as bi,_a as bj,Wa as bk,Or as bl,Gr as bm,Ls as bn,W as bo,ga as bp,Ks as bq,bs as br,Yr as bs,ts as bt,Qa as bu,_ as c,Ma as d,Ds as e,Rt as f,Js as g,ia as h,Dr as i,_s as j,oa as k,ss as l,Us as m,ns as n,fa as o,Zs as p,Qs as q,ja as r,la as s,Ys as t,os as u,Ve as v,ws as w,aa as x,Xr as y,Vt as z};
