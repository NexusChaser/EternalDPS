# EternalDPS — arquitectura del sistema de guardado

> **Eternal Data Persistence System.** Sigue la convención de `CycloneAMS`: un nombre de Gaia
> Memory más el dominio, y una clase *Driver* que lo gobierna. *Eternal* se eligió porque en
> inglés describe literalmente lo que hace el sistema —datos que sobreviven a la sesión—, así que
> funciona como nombre técnico para alguien que no conozca la referencia.

Documento de diseño. Describe **qué se construye y por qué**, no el código. Lo que aparece como
dato verificado lleva su fuente al final.

- **Nombre:** EternalDPS · corto: **Eternal**
- **Paquete:** `com.nexuschaser.eternaldps`
- **Namespace:** `NexusChaser.EternalDPS`
- **Fachada del núcleo:** `EternalDataDriver` (paralelo a `CycloneAudioDriver`)
- **Extensión:** `.etm`
- **Cabecera mágica:** `ETM1`

---

## 1. Objetivos y no-objetivos

### Objetivos

1. **Reutilizable** entre proyectos de Unity sin copiar y pegar carpetas.
2. **Núcleo independiente del motor.** `Eternal.Core` no referencia `UnityEngine`. Si otro motor
   aporta una implementación de almacenamiento y un serializador, funciona ahí.
3. **PC, móvil y WebGL** desde el primer día. Consola, preparada pero no implementada.
4. **Modular.** Cifrado, formato binario y Steam son piezas que se enchufan.
5. **Listo para binario** sin romper los guardados ya escritos en JSON.
6. **Herramientas visuales**, con la capa gráfica separada del núcleo para que cada motor
   construya la suya (§11).
7. **Verificable y testeable** sin abrir Unity (§12 y §13).

### No-objetivos (deliberados)

- **No fotografía escenas ni componentes.** Nada de GUIDs por GameObject. Ese enfoque ata el
  formato a la jerarquía, y un parche que toque prefabs invalida las partidas de los jugadores.
- **No hay API estática tipo `Eternal.Set("clave", valor)`.** Es cómoda y es exactamente cómo se
  acaba con datos esparcidos por doscientos sitios y ninguna migración posible.
- **No descubre por reflexión qué guardar.** Explícito, o las migraciones dejan de ser tratables.
- **No abstrae la nube todavía.** Steam Auto-Cloud no necesita código.
- **No es anti-trampas.** Ver §6.

---

## 2. Qué es reutilizable y qué no

| capa | ¿en el paquete? | por qué |
|---|---|---|
| Almacenamiento (bytes → dispositivo) | sí, 100 % | archivo, WebGL, consola |
| Contenedor (compresión, cifrado, integridad, cabecera) | sí, 100 % | transformaciones puras de bytes |
| Serialización (objeto ↔ bytes) | sí, 100 % | intercambiable |
| Ranuras y catálogo | sí, ~90 % | la forma es universal, el contenido no |
| Versionado y migraciones | sí, ~80 % | el mecanismo se reutiliza, las migraciones son del juego |
| **API de inspección y edición** | **sí, 100 %** | sin motor: es lo que permite que cada uno haga su UI |
| Capa visual de las herramientas | **no** | cada motor implementa la suya sobre la API |
| Política de guardado (cuándo) | parcial, ~60 % | los disparadores son universales, los momentos no |
| **Modelo de datos** | **no** | dominio puro del juego |
| **Captura y restauración** | **no** | dominio puro del juego |

---

## 3. Los dos ejes: ámbito y tipo

«Perfil» significa dos cosas distintas y mezclarlas enturbia el diseño. Son **ejes independientes**:

- **Ámbito** — a quién pertenece el dato: máquina → cuenta → ranura.
- **Tipo** — qué clase de registro es: ajustes → progreso → sesión.

|  | **máquina** | **cuenta / jugador** | **ranura (partida)** |
|---|---|---|---|
| **ajustes** | calidad gráfica, resolución, dispositivo de audio | idioma, volumen, accesibilidad | — |
| **progreso** | — | logros globales, galería | nivel, inventario, misiones |
| **sesión** | — | — | posición exacta, estado del mapa |

De la matriz cae una regla que se incumple constantemente: **borrar una ranura no puede borrar el
progreso de cuenta.** Si el progreso de cuenta vive dentro del archivo de la ranura, ese bug es
inevitable.

### Caso degenerado: este juego

Ámbitos: **máquina** y **cuenta**; ranura, la implícita única. Tipos: **ajustes** y **sesión**.
No sobra nada ni se fuerza nada — esa es la prueba de que la abstracción no está inflada.

### La clave

```
EternalKey = (Scope, Kind, RecordId, SlotId)
```

`SlotId` **siempre existe**, con `SlotId.Default` por defecto. Este juego llama sin especificar
ranura y obtiene la única; un RPG pasa un id real. **No hay dos caminos de código, ni una rama
`if (usaRanuras)`, ni nada muerto que desactivar.**

---

## 4. Perfiles de guardado (políticas)

Cada tipo de registro declara su política. Aquí «partida completa» y «solo progreso» dejan de ser
dos sistemas y pasan a ser dos configuraciones del mismo.

| campo | valores | para qué |
|---|---|---|
| `Lifetime` | `Permanent` \| `Session` | si se borra al consumirse |
| `Cloud` | `Sync` \| `Local` | los ajustes de máquina **nunca** van a la nube |
| `OnMigrationFailure` | `Fail` \| `DiscardAndNotify` | ver abajo |
| `OnIntegrityFailure` | `TryBackup` \| `DiscardAndNotify` \| `Fail` | ver §12 |
| `Conflict` | `Newest` \| `PreferLocal` \| `Merge(custom)` | resolución entre máquinas |
| `Backups` | `int` | copias del anterior por registro |

La diferencia real entre progreso y sesión está en dos filas:

- **Progreso**: si falla hay que **recuperarlo como sea** — perderlo es perder el historial del
  jugador. Conflicto: unión o máximo.
- **Sesión**: si falla, lo correcto es **descartarla y avisar** («no se pudo restaurar la
  partida»), no reventar ni corromper. Conflicto: la más reciente.

---

## 5. El formato `.etm`

Pieza central y **contrato público**: en cuanto el juego 1 se publique, el formato solo se podrá
extender, nunca cambiar.

```
┌─ PREÁMBULO — en claro, nunca cifrado ──────────────────────┐
│  0   4   magic            "ETM1"                            │
│  4   1   containerVersion versión de ESTE formato           │
│  5   1   flags            bit0 metadatos · bit1 firmado     │
│  6   2   metaLen          longitud del bloque METADATOS     │
│  8   4   bodyLen          longitud del CUERPO               │
│ 12   1   serializerId     con qué se serializó el cuerpo    │
│ 13   1   keyId            con qué clave se firmó            │
│ 14   1   transformCount                                     │
│ 15   n   transformIds[]   1 byte cada una, EN ORDEN         │
└─────────────────────────────────────────────────────────────┘
┌─ METADATOS — codificación propia, sin comprimir, NO cifrados ─┐
│  productId · schemaVersion · savedAtUtc · appVersion         │
│  slotName · playtime · thumbOffset/thumbLen · campos juego   │
└─────────────────────────────────────────────────────────────┘
┌─ CUERPO — serializado + transformaciones aplicadas ────────┐
└─────────────────────────────────────────────────────────────┘
┌─ FIRMA ─────────────────────────────────────────────────────┐
│  32   HMAC-SHA256 sobre TODO lo anterior                     │
└─────────────────────────────────────────────────────────────┘
```

### Seis decisiones y su porqué

**1. Los metadatos van aparte y sin cifrar.** La pantalla de selección tiene que dibujar seis
ranuras con «Nivel 34 · 12 h 20 min» **sin deserializar seis partidas completas**. Y el corolario
que casi nadie implementa: deben poder leerse **aunque el cuerpo esté corrupto**, para que una
partida dañada no desaparezca de la lista y el jugador pueda al menos borrarla. Que no vayan
cifrados es una concesión consciente: el nivel y el tiempo jugado no son secretos.

> **Corregido al implementar.** Este bloque decía «serializados, comprimidos». Ninguna de las dos
> cosas resultó correcta y se cambiaron:
>
> - **No usan el `ISerializer` del juego, sino una codificación binaria fija del núcleo.** Los
>   metadatos son lo que responde «¿este archivo es mío?», y un build tiene que poder responderlo
>   sobre un archivo escrito por otro build cuyo serializador no lleva compilado. Si dependieran
>   del serializador enchufable, que faltara un módulo opcional dejaría el guardado ilegible **y
>   además** no identificable, así que ni siquiera se podría listar o borrar. Es justo el caso que
>   la §10 necesita que funcione.
> - **No se comprimen.** El bloque son unos cientos de bytes: comprimirlo no ahorra nada que
>   justifique inflarlo en cada entrada de la lista de ranuras, y lo único grande —la
>   miniatura— ya llega comprimida como PNG o JPEG.
>
> La miniatura vive **dentro** del bloque de metadatos, con `thumbOffset` relativo al inicio del
> bloque. Consecuencia a tener en cuenta: como `metaLen` son 2 bytes, **el bloque entero no puede
> pasar de 64 KB**, y en la práctica eso es el techo de la miniatura.

**2. El HMAC cubre el preámbulo, no solo la carga.** Si solo firmara el cuerpo, alguien editaría
los `transformIds` para declarar «sin cifrado» y se llevaría la protección por delante. Esto
cierra ese ataque de degradación, y está **cubierto por un test** (§13).

**3. Los `transformIds` son una lista ordenada, no un flag.** El archivo dice qué hay que
deshacerle. Eso da **compatibilidad hacia adelante**: los archivos escritos *antes* de añadir
cifrado se siguen leyendo *después* de añadirlo.

**4. `serializerId` cumple el requisito de «listo para binario».** Un build futuro escribe `0x02`
(binario) y **sigue leyendo** los `0x01` (JSON) mientras el adaptador JSON siga compilado. Se
puede migrar de forma perezosa: leer JSON, escribir binario. Sin conversión masiva ni día D.

**5. Un solo archivo por ranura.** La cuota de Steam Cloud es por bytes **y por número de
archivos**. Con el preámbulo al principio, listar es abrir, leer el primer bloque y cerrar.

**6. El `keyId` va en el preámbulo, antes de la firma.** Tiene que poder leerse **sin haber
verificado nada**, porque es lo que dice con qué clave verificar. Se reserva el byte **aunque solo
exista una clave**: el formato es contrato público y añadir un byte después es subir la versión
del contenedor. Habilita la rotación descrita en §6.

### Registros de identificadores — nunca se reutiliza un número

```
TRANSFORMACIONES               SERIALIZADORES
0x01  Deflate                  0x01  JSON (Newtonsoft, UTF-8)
0x02  AES-256-CBC              0x02  Binario (MemoryPack)  ← reservado
0x03..0x7F  reservado Eternal  0x03..0x7F  reservado Eternal
0x80..0xFF  libre del juego    0x80..0xFF  libre del juego
```

---

## 6. Integridad y cifrado: dos cosas distintas

- **Confidencialidad** — que no lo puedan *leer*. Se consigue cifrando.
- **Integridad** — que no lo puedan *modificar* sin que te enteres. Se consigue firmando.

Para una partida importa la segunda. Da igual que un jugador lea su propia puntuación; lo que no
puede es ponerla a 999 y que el juego se la trague. Y **cifrar no da integridad**: con solo AES,
alguien corrompe o empalma el texto cifrado y el juego carga basura sin saberlo.

**Perfil por defecto:** `Deflate` + `HMAC-SHA256`. El archivo es basura binaria en un editor de
texto, cambiarle la extensión no da nada, y cualquier bit alterado se detecta. El Deflate además
reduce el tamaño, lo que ayuda con la cuota de la nube.

**AES es opcional**, en un ensamblado aparte. Si se activa: **cifrar primero y firmar después**,
sobre el texto cifrado. El orden inverso tiene problemas conocidos.

Como el HMAC detecta manipulación **y** corrupción accidental, no hace falta un CRC aparte.

> **Esto es ofuscación, no seguridad.** La clave está en el ejecutable y quien tenga ganas la
> saca. Es cierto de todo guardado local. Lo que sí consigue al 100 % es frenar la edición casual
> y detectar corrupción. Si algún día hay tablas de clasificación, la respuesta no es una clave
> mejor: es autoridad en servidor.

La clave no se mete como cadena literal, porque aparece haciendo `strings` al binario: se deriva
en runtime de varias constantes separadas.

### La clave la pone el juego, no el paquete

Dos reglas, y la segunda es consecuencia directa de que el paquete viva en un **repositorio
público**:

1. **Por juego, nunca compartida.** Una filtración no puede cruzar títulos.
2. **El paquete no trae clave por defecto.** Cualquier clave que viajase dentro del paquete
   estaría publicada en GitHub, y entonces la firma no protegería nada en ningún juego que lo
   use. El paquete define un `IKeyProvider`; cada juego lo implementa en su propio ensamblado, y
   el núcleo **falla ruidosamente** si se le pide firmar sin habérselo dado.

#### Qué fija el paquete y qué elige cada proyecto

Repartir esto mal tiene consecuencias. La regla es: **libertad donde equivocarse es inocuo,
imposición donde equivocarse es fatal.**

| | decide | por qué |
|---|---|---|
| fuente de aleatoriedad | **el paquete** | es lo que más se falla. Si cada proyecto improvisa, alguien acabará usando `System.Random` o una contraseña, y no hay nada que salvar después |
| derivación (HKDF) | **el paquete** | congelada; cambiarla invalidaría todas las claves existentes |
| tamaño mínimo | **el paquete** | suelo de 32 bytes |
| tamaño concreto | el proyecto | dentro de la banda útil, abajo |
| dónde vive el material | el proyecto | repositorio privado del juego, o secreto de CI |
| cuándo se rota | el proyecto | depende de su ciclo de publicación |

**Sobre el tamaño, un dato que ahorra esfuerzo:** HMAC reduce con hash cualquier clave más larga
que el bloque de la función, que en SHA-256 son **64 bytes**. Una clave de 512 bytes acaba
convertida en 32 antes de usarse, así que no compra absolutamente nada. La banda con sentido es
**32 a 64 bytes, y 32 es el estándar**. Más grande no es más seguro; es solo más grande.

### Rotación: no es una clave, es un conjunto

Hay una tensión real entre dos cosas que las dos queremos. Una clave estable entre versiones es
justo lo que hace que un guardado viejo siga cargando **y** justo lo que hace permanente una
filtración. No se resuelve eligiendo un lado: se resuelve **versionando la clave**.

- El juego lleva la clave **actual** más todas las **retiradas**, estas últimas solo para leer.
- Se **escribe** siempre con la más nueva. Se **lee** con la que el archivo declare en su `keyId`.
- Al cargar un archivo firmado con una clave retirada, **se refirma con la actual en el siguiente
  guardado**. Silencioso y automático: el jugador no hace nada.
- Política de retirada por clave: `Active` → `ReadOnly` → `Warn` → `Rejected`. Para cuando una
  llega a `Rejected`, casi todos los guardados vivos ya se refirmaron solos.

Así se obtienen las dos cosas: **migrables**, porque la clave vieja sigue presente para leer, y
**acotados**, porque si una clave se filtra, la siguiente versión la deja sin valor para firmar.

> **El techo, dicho claro.** La clave nueva también está en el binario, así que la rotación no
> detiene a alguien decidido. Lo que detiene es que **una clave publicada siga sirviendo** — y ese
> es el escenario realista: no un tramposo suelto, sino un editor de guardados circulando por un
> foro.

Si algún día se quisieran subclaves distintas por tipo de registro, HKDF las da gratis con su
parámetro `info`. Hoy no hace falta.

### Cómo se rota: la herramienta

Una política sin herramienta no se ejecuta el día que hace falta. El paquete incluye un
**generador de claves**, disponible como ventana de editor y como comando del CLI:

- Material aleatorio de **CSPRNG** (`RandomNumberGenerator`), nunca `System.Random`.
- Toma el **siguiente `keyId` libre** y **se niega a sobrescribir uno existente**. Un `keyId`
  reutilizado haría que dos claves distintas reclamasen los mismos archivos.
- Emite un **archivo de código fuente** con el conjunto completo —claves actual y retiradas, con
  su estado—, troceado para que ninguna aparezca como literal contiguo en el binario.
- Ese archivo vive en el **repositorio privado del juego**, jamás en el del paquete. Si algún día
  el proyecto lo justifica, el camino de mejora es inyectarlo en el build desde un secreto de CI.

Un `ScriptableObject` sería peor que el código: un asset se extrae de un build con más facilidad.

### El procedimiento del día malo

Cuando una clave se filtra:

1. **Generar** `keyId` siguiente. Pasa a `Active`; la anterior baja a `ReadOnly`.
2. **Publicar el parche.** Desde ya, todo lo que se guarde sale con la clave nueva.
3. **Esperar.** Los guardados legítimos **se refirman solos** conforme la gente juega. Este paso
   es el que hace que el siguiente duela poco.
4. **Rechazar la clave vieja** cuando se estime que la mayoría ha migrado.

> **El paso 4 es el que corta la filtración, y el que tiene precio.** Mientras la clave vieja siga
> en `ReadOnly`, el juego acepta lo que ella firme: **el editor filtrado sigue funcionando**. Y el
> día que se rechaza, quien no haya abierto el juego desde antes de la rotación **pierde su
> partida**, porque la suya sigue firmada con la vieja.
>
> Ese compromiso es inherente: **rechazar pronto corta la filtración y deja fuera a los rezagados;
> rechazar tarde no deja a nadie fuera y mantiene la puerta abierta.** No hay tercera opción. Lo
> que sí se puede es llegar a esa decisión con la mayoría ya migrada, que es para lo que sirven
> los pasos 1 a 3.

### Extensión diseñada y no implementada: suelo de clave por ranura

Cierra la ventana del paso 3 sin tener que esperar al 4. **No se implementa para este juego**,
pero queda diseñada para que añadirla más adelante sea mecánico.

**Cómo funciona.** Se guarda el `keyId` más alto visto por ranura y se **rechazan firmas de claves
anteriores para esa ranura**. Una partida ya migrada a la clave 2 no puede volver a aceptar algo
firmado con la 1, así que el editor filtrado deja de servir contra esa ranura en cuanto el jugador
guarda una vez, sin esperar a retirar la clave vieja globalmente.

**Lo importante: no toca el formato.** El `keyId` ya está en el preámbulo, que es lo único que la
comprobación necesita leer. El suelo vive en un **registro nuevo** —de ámbito cuenta, firmado con
la clave actual—, y los registros son aditivos por diseño. O sea que **no hay que reservar nada hoy
ni subir la versión del contenedor mañana**: es una comprobación más y un registro más.

Que el suelo vaya firmado con la clave **actual** es lo que lo hace fiable: quien tenga la clave
vieja filtrada no puede rebajarlo.

**Dos reglas para cuando se implemente:**

- **Si el suelo falta o está corrupto, se falla abierto** —sin suelo— nunca cerrado. Un marcador
  dañado no puede dejar al jugador sin sus partidas.
- **Rompe la restauración de copias antiguas.** Un `.bak` firmado con una clave anterior al suelo
  sería rechazado. Habría que exceptuar explícitamente la restauración manual desde la herramienta.

Ese segundo punto es el coste real, y es la razón de no activarlo en un party game sin
clasificaciones: se paga en recuperación ante desastres a cambio de cerrar una ventana que aquí
casi no importa. En un juego con progresión competitiva el cálculo cambia.

> **La derivación también está congelada.** Si cambiase el HKDF, todas las claves existentes
> dejarían de reproducirse. Forma parte del contrato, igual que el formato.

### Cuando la integridad falla, no se acusa a nadie

Una firma que no cuadra **no distingue** manipulación de corrupción: un sector defectuoso y un
editor de guardados dan el mismo resultado. Así que la reacción es la misma y es amable — intentar
el `.bak`, y si no hay, ofrecer empezar de nuevo. Nunca un mensaje que acuse al jugador de hacer
trampas, porque una parte de las veces será un disco que falla.

---

## 7. Ensamblados

Los opcionales usan **version defines**, así que ni siquiera compilan si su dependencia no está.
Eso es lo que hace que «implementado pero este juego no lo usa» funcione de verdad.

| ensamblado | referencias | notas |
|---|---|---|
| `Eternal.Core` | **ninguna de Unity** | .NET Standard 2.1. Testeable en CI sin abrir Unity |
| `Eternal.Tooling` | Core, **ninguna de Unity** | inspección, edición y reempaquetado (§11) |
| `Eternal.Serialization.Newtonsoft` | Core + Newtonsoft | version define sobre `com.unity.nuget.newtonsoft-json` |
| `Eternal.Serialization.Binary` | Core + MemoryPack | **reservado**, no se implementa ahora |
| `Eternal.Crypto` | Core | AES. Un juego que no lo quiera no lo envía en el build |
| `Eternal.Unity` | Core + UnityEngine | almacenes, rutas, ciclo de vida, `link.xml` |
| `Eternal.Unity.Editor` | Eternal.Unity + Tooling | **solo** las ventanas. Toda la lógica está en Tooling |
| `Eternal.Cli` | Tooling | opcional: `verify` / `dump` / `repack` para CI y soporte |
| `Eternal.Steam` | Eternal.Unity + wrapper | `SteamRemoteStorageStore`, la vía de la API. Con Auto-Cloud no se referencia |
| `Eternal.Tests` | Core + Tooling | suite de conformidad y pruebas de formato (§13) |

Que `Eternal.Core` y `Eternal.Tooling` no dependan de `UnityEngine` es lo que cumple el requisito
de «otro motor»: otro motor aporta su `IStore`, su `ISerializer` y su UI, y el resto vale tal cual.

**Distribución por git URL, nunca copiando la carpeta.** Versionado semántico y changelog, porque
el juego 2 estará en una versión anterior y no se pueden actualizar los dos a la vez.

---

## 8. Abstracciones del núcleo

```
ISerializer      objeto ↔ bytes.           Expone FormatId.
IDocumentSerializer  (opcional)  además expone el cuerpo como árbol editable.
IByteTransform   bytes → bytes + inversa.  Expone TransformId.
IStore           lee/escribe/borra/lista bytes por clave. Declara Capabilities.
IClock           para poder testear sin reloj real.
IEternalLog      para no imponer Debug.Log al núcleo.
```

### La API es asíncrona, y no es una preferencia

En WebGL, Unity **no llama solo a `FS.syncfs()`** cuando escribes un archivo normal: hay que
forzarlo desde un plugin `.jslib`, y ese volcado es **asíncrono**. Si `IStore` fuera síncrono,
`Save()` volvería antes de que el dato estuviese realmente en IndexedDB y en WebGL **nunca sería
correcto**. Las APIs de save data de consola también son asíncronas. Por eso el núcleo es
asíncrono de raíz.

### Capacidades declaradas por el almacén

```
[Flags] StoreCapabilities { AtomicReplace, List, Delete, RandomAccess }
```

La atomicidad es una **capacidad**, no una garantía universal. El núcleo degrada con elegancia:

| almacén | capacidades | estrategia de escritura |
|---|---|---|
| archivo (PC, Linux, Mac, Android, iOS) | todas | escribir `.part` → reemplazar |
| WebGL (IDBFS) | sin `AtomicReplace` | copia previa → escribir → `syncfs` → verificar |
| Steam Cloud API | sin `AtomicReplace`, sin `RandomAccess` | `FileWrite` reemplaza entero |
| consola | varía | la que permita la plataforma |

---

## 9. Disposición en disco

```
<persistentDataPath>/
  machine/                      ← NUNCA a la nube
    display.etm
  account/
    prefs.etm
    progress.etm
  slots/
    catalog.etm                 ← caché, no fuente de verdad
    <guid>/
      save.etm
      save.etm.bak
```

**El id de ranura es un GUID opaco y estable, jamás su posición.** Si las ranuras fueran 1..6 por
orden, borrar la 2 renumeraría todo y rompería la sincronización en la nube y cualquier
referencia. Es la misma lección que ya costó un susto en este proyecto con el enum `ChannelType`
del sistema de audio: **el valor serializado no puede depender de la posición.** El nombre y el
orden de visualización son datos aparte.

### El catálogo es una caché

Si se escribe el cuerpo y el proceso muere antes de actualizar el índice, queda un huérfano. Por
eso el orden es **cuerpo primero (atómico), catálogo después**, y el catálogo es **reconstruible
escaneando** `slots/*/`. Una inconsistencia se repara sola en vez de perder partidas.

### Autoguardado, rápido y manual no son tres sistemas

Son tres ranuras con políticas distintas: **autoguardado** es un anillo de N que rotan,
**rápido** es una ranura reservada siempre la misma, **manual** las nombra y borra el jugador. Un
juego que solo quiere autoguardado declara un anillo de 1.

---

## 10. Versionado y migraciones

Hay **dos versiones independientes** y confundirlas es un error clásico:

- `containerVersion` — el formato del sobre (§5).
- `schemaVersion` — la forma de los datos del juego, en los **metadatos**.

Que `schemaVersion` viva en los metadatos es deliberado: **se puede leer sin deserializar el
cuerpo**, así que se decide si migrar, rechazar o descartar *antes* de intentar cargar nada.

### Dos niveles de migración

1. **Tipada** (mínimo común denominador): deserializar con el POCO viejo → mapear al nuevo.
   Funciona con cualquier serializador, binario incluido.
2. **Documental** (optimización): manipular el árbol directamente. Solo la ofrecen los
   serializadores que implementan `IDocumentSerializer`.

El núcleo exige la tipada y permite la documental cuando está disponible. **Esto es lo que
mantiene abierta la puerta al binario**: si las migraciones solo funcionaran sobre JSON, el día
que se pase a binario habría que reescribirlas todas.

### Identidad del producto: sobrevivir a un cambio de nombre

`Application.persistentDataPath` es `<raíz>/<Company>/<Product>`. Si se renombra cualquiera de
los dos —cosa que en una jam pasa **casi siempre al final**— Unity empieza a mirar una carpeta
nueva y vacía. Los guardados del jugador siguen en disco, huérfanos. Dejar a la gente tirada así
no es aceptable en un juego publicado.

La solución es una inversión sencilla: **la identidad del juego vive dentro del archivo, no en la
ruta.**

- Los metadatos llevan un **`productId`**: un GUID generado **una sola vez** y congelado para
  siempre. No es el nombre del producto, no se traduce, no se cambia al renombrar.
- La configuración del juego declara el `productId` actual y, si hiciera falta, una lista de
  **identidades heredadas** aceptadas.

Con eso, renombrar deja de ser un evento: un `.etm` es «nuestro» aunque esté en una carpeta con
otro nombre.

**Orden de comprobación**, de lo más barato a lo más caro — y ninguna etapa deserializa el cuerpo:

1. ¿La cabecera mágica es `ETM1`?
2. ¿`containerVersion` está soportada?
3. ¿El `productId` coincide con el actual o con una identidad heredada?
4. ¿Hay camino de migración desde ese `schemaVersion`?
5. Solo entonces: adoptar y migrar.

Fíjate en que esto se apoya en la decisión de §5 de dejar los metadatos **fuera del cuerpo y sin
cifrar**: se puede interrogar un archivo ajeno y decidir si es adoptable sin descifrar nada.

**Adopción automática en runtime, no solo una herramienta.** Una herramienta de editor no salva al
jugador, así que la adopción ocurre sola: si la carpeta actual está vacía y hay una carpeta
hermana con guardados compatibles, se adoptan al primer arranque. Reglas:

- **Se copia, nunca se mueve.** El original queda como red de seguridad.
- Se escribe una marca de adopción para que no se repita en cada arranque.
- Si hay varias candidatas compatibles, gana la de `savedAtUtc` más reciente, y se registra.
- El jugador recibe un aviso de que se han recuperado sus datos. Nunca en silencio.

La herramienta de editor (§11) añade lo que el runtime no debe hacer solo: comparar dos archivos
cualesquiera, forzar una adopción, y ver **por qué** un archivo se consideró incompatible.

### WebGL: automático dentro del mismo origen, manual fuera

En WebGL no hay carpetas hermanas que recorrer, pero **sí hay forma**. `indexedDB.databases()` es
**Baseline desde mayo de 2024**, y la documentación de MDN señala como uso previsto justamente
*«limpiar bases de datos creadas por versiones anteriores de la aplicación»* — nuestro caso exacto.

El almacén de WebGL puede entonces: enumerar las bases del origen desde su `.jslib`, localizar la
del IDBFS antiguo, leer las entradas y pasar los bytes al mismo `CanAdopt` de siempre. Con
detección de característica, para que un navegador viejo simplemente se salte la adopción en vez
de romperse.

> **Lo que sigue siendo imposible: cruzar orígenes.** Si el juego se mueve de `itch.io/tu-juego` a
> `tudominio.com`, no hay nada que hacer. Es una frontera de seguridad del navegador —esquema,
> host y puerto—, no una limitación de Unity ni de este sistema.

### La red que cubre todo lo demás: exportar e importar

Para lo que la adopción automática no alcanza —cambio de dominio, pasar de PC a web, mover una
partida entre dispositivos sin nube— el sistema expone **exportar e importar** un guardado como
archivo portátil, o como cadena de texto para poder pegarla.

Es la única vía que funciona entre orígenes distintos y entre plataformas distintas, y en un party
game resulta además una función simpática por sí misma. Reutiliza el contenedor tal cual: lo que se
exporta **es** un `.etm`, con su `productId` y su firma, así que importar pasa por las mismas
comprobaciones que cualquier otra adopción.

---

## 11. Herramientas: núcleo sin cara, capas visuales por motor

Este es el requisito de «cada motor implementa su propia versión visual». La forma de cumplirlo
no es tener buena voluntad, es **una regla**:

> **Las herramientas del editor no tienen acceso privilegiado.** Usan exactamente la misma API
> pública que usaría la herramienta de cualquier otro motor. Si una ventana de Unity necesita
> algo que la API no expone, **se añade a la API**, nunca una puerta trasera `internal`.

Sin esa regla, «otro motor puede hacer la suya» es una aspiración; con ella, es un hecho
comprobable.

### `Eternal.Tooling` — la API sin cara

Vive fuera de Unity, en .NET Standard. Ofrece:

| operación | qué hace |
|---|---|
| `OpenAsync(bytes)` | devuelve un `EternalDocument` en memoria, editable |
| `document.Preamble` | versión del contenedor, serializador, cadena de transformaciones |
| `document.Metadata` | metadatos tipados **y** crudos, legibles aunque el cuerpo falle |
| `document.Integrity` | el veredicto de §12, sin excepciones |
| `document.Body` | árbol editable **si** el serializador implementa `IDocumentSerializer` |
| `document.RepackAsync(opciones)` | vuelve a bytes, recalculando longitudes y firma |
| `EternalDocument.Create(modelo, perfil)` | fabrica un guardado desde cero |
| `Catalog.ScanAsync(store)` | reconstruye el índice de ranuras leyendo solo preámbulos |
| `VerifyAsync(store, filtro)` | informe de integridad en lote (§12) |

**Degradación honesta para el binario:** si el serializador no expone árbol, el editor cae a
**edición tipada** — deserializa en el modelo, lo edita con el inspector normal del motor y
vuelve a serializar. Es la misma distinción que en las migraciones (§10), y por eso son
coherentes: ningún camino depende de que el formato sea JSON.

### Ventanas de Unity (`Eternal.Unity.Editor`)

Son clientes finos. Toda la lógica está en Tooling.

1. **Save Browser** — lista ámbitos, ranuras y registros con sus metadatos, **sin cargar
   cuerpos**. Muestra en rojo las entradas dañadas, con opción de borrar o restaurar del `.bak`.
2. **Save Inspector** — abre un `.etm`: preámbulo, metadatos, veredicto de integridad, y el
   cuerpo editable campo a campo. Guardar reempaqueta y **refirma**.
3. **Save Forge** — crea un guardado desde cero o desde una plantilla. No es un lujo: permite a QA
   generar una partida en un estado concreto —«justo antes del jefe final», «con todo
   desbloqueado»— y probar un minijuego sin jugar cuarenta minutos.
4. **Export / Import plano** — vuelca a JSON legible y vuelve a empaquetar. Es el camino de
   legibilidad para depurar sin que el build de release sepa hacerlo.
5. **Integrity Report** — pasa `VerifyAsync` sobre una carpeta y saca la tabla.
6. **Load into Play** — marca un guardado como pendiente para que el juego lo cargue al entrar en
   play. Pegamento específico de Unity.

### CLI opcional (`Eternal.Cli`)

Como Tooling no depende del motor, un ejecutable `dotnet` sale casi gratis: `eternal verify`,
`eternal dump`, `eternal repack`. Sirve en CI y para revisar el guardado que mande un jugador sin
abrir el editor. Además es **la prueba viva** de que la API no depende de Unity: si el CLI
compila, la regla de arriba se está cumpliendo.

### Si una build de desarrollo escribe JSON en claro

Dos condiciones **obligatorias**:

1. Detrás de `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, para que no exista en release.
2. Con una extensión que **no** esté en el patrón de Steam Auto-Cloud.

Lo mismo para los temporales: se llaman `.part` precisamente para que el patrón `*.etm` no los
recoja.

---

## 12. Integridad, diagnóstico y recuperación

### El resultado de cargar es un veredicto, no una excepción

Un guardado que no carga no es un caso excepcional: es un caso esperado que tiene que tener
respuesta de producto. Por eso `LoadAsync` devuelve un resultado cerrado:

| resultado | significado | reacción típica |
|---|---|---|
| `Ok` | cargado | — |
| `NotFound` | no existe | partida nueva |
| `NotEternalFile` | la cabecera mágica no cuadra | ignorar, no es nuestro |
| `ContainerTooNew` | `containerVersion` mayor que el soportado | «guardado de una versión más nueva» |
| `UnknownTransform` / `UnknownSerializer` | id no registrado en este build | falta un módulo opcional |
| `IntegrityFailed` | el HMAC no cuadra | probar `.bak`, luego la política del perfil |
| `SchemaTooNew` | `schemaVersion` del futuro | rechazar sin tocar el archivo |
| `MigrationFailed` | la cadena de migración se rompió | política del perfil |
| `Corrupt` | parseo fallido pese a firma válida | bug nuestro: registrar y avisar |

Cada uno se cruza con `OnIntegrityFailure` / `OnMigrationFailure` del perfil (§4). Ahí está la
conexión: **el veredicto no decide qué hacer, la política sí.** Un progreso intenta el `.bak`;
una sesión se descarta y se avisa.

**Nunca en silencio.** Si se recupera de un `.bak`, el jugador debe saber que se ha vuelto a un
punto anterior.

### Verificación sin cargar

`VerifyAsync` lee preámbulo y metadatos y recalcula el HMAC **en flujo, sin deserializar el
cuerpo**. Es barato de pasar sobre una carpeta entera, y es lo que alimenta el *Integrity Report*
y el `eternal verify` del CLI.

### Copias

`Backups = N` por perfil. Se escribe el nuevo, se rota el anterior. Barato, y en un RPG es la
diferencia entre «hemos perdido tus cuarenta horas» y «hemos recuperado tu guardado anterior».

---

## 13. Pruebas

Como `Eternal.Core` y `Eternal.Tooling` no dependen de `UnityEngine`, **la suite corre en CI sin
abrir Unity**. Ese es el beneficio concreto de haber mantenido el núcleo limpio.

### Pruebas de formato

- **Ida y vuelta.** Para cada serializador × cada cadena de transformaciones:
  `leer(escribir(x)) == x`.
- **Archivos dorados.** `.etm` **commiteados** escritos por la versión N deben seguir cargando en
  la N+1. Es la red de seguridad de «el formato es un contrato público». Cada vez que cambie el
  contenedor se **añade** un dorado nuevo; los viejos **no se tocan jamás**.
- **Manipulación.** Voltear un bit en cada región —preámbulo, metadatos, cuerpo, firma— y
  comprobar que sale el `LoadResult` correcto.
- **Degradación.** Editar la cadena de transformaciones para declarar «sin cifrado» y comprobar
  que la integridad falla. Es el ataque de §5 decisión 2, convertido en test.
- **Truncamiento.** Cortar el archivo en N posiciones y comprobar que no revienta y da un
  resultado sensato.
- **Migraciones.** Un fixture por cada `schemaVersion` histórica, cargado hasta la actual.

### Suite de conformidad de almacenes

Una batería compartida que **cualquier `IStore` debe pasar**: escribir, leer, sobrescribir,
borrar, listar, claves inexistentes, claves con caracteres raros, concurrencia. Es así como un
backend nuevo —WebGL, consola, otro motor— demuestra que es correcto en vez de suponerlo.

### Inyección de fallos

Un decorador de `IStore` que falla en la escritura número N. Con él se comprueba la propiedad que
más importa: **si el proceso muere a mitad de guardar, en disco queda el archivo viejo o el
nuevo, nunca uno a medias.** Se prueba también sobre el catálogo, para verificar que un huérfano
se repara escaneando.

### Prueba de independencia del motor

Que `Eternal.Cli` compile y pase sus pruebas **es** la prueba de que el núcleo no se ha
contaminado de Unity. Si un día deja de compilar, alguien metió una dependencia donde no debía.

---

## 14. Plataformas

### WebGL

`persistentDataPath` va a **IndexedDB** a través de IDBFS. Unity **no sincroniza automáticamente**
las escrituras de archivo arbitrarias. El almacén de WebGL necesita:

- un plugin `.jslib` en `Assets/Plugins/WebGL/` que exponga `FS.syncfs(false, cb)`,
- llamarlo tras cada escritura,
- y **no dar el guardado por hecho hasta que el callback vuelva**.

Sin esto, el jugador cierra la pestaña y pierde la partida. Es el fallo número uno de guardado en
WebGL.

### IL2CPP (Android, iOS, WebGL, consola)

IL2CPP elimina el código que cree no usado, **incluidos los constructores de tipos que solo se
instancian por reflexión**. El síntoma es que la deserialización falla **en runtime**, no al
compilar, y solo en el build. Mitigaciones:

- El paquete incluye un `link.xml` que preserva `JsonConvert` y `DefaultContractResolver`.
- **Los modelos del juego viven en su propio asmdef**, y ese ensamblado se preserva entero. Más
  limpio que ir tipo por tipo.
- `com.unity.nuget.newtonsoft-json` **≥ 3.0** ya trae `AotHelper` y los arreglos de stripping.
  Este proyecto tiene **3.2.2**, así que está cubierto.

### Momentos de guardado

`OnApplicationPause(true)` y `OnApplicationFocus(false)`. **Nunca `OnApplicationQuit`**: en
Android y WebGL no se llama de forma fiable. Con antirrebote, para no escribir en cada cambio de
un slider.

### Steam: las dos vías, y elige el desarrollador

Steam ofrece dos caminos con propiedades **distintas**, y el paquete implementa **los dos**. No hay
una respuesta universal, así que la decisión se deja en el proyecto que lo consuma: se elige el
almacén al arrancar, en una línea de composición.

| | **Auto-Cloud** | **Cloud API (`ISteamRemoteStorage`)** |
|---|---|---|
| código de Steam | **ninguno** | necesita el wrapper y la inicialización |
| build Steam vs no-Steam | **idénticas** | difieren en el almacén elegido |
| identificación | por **ruta** en disco | por **nombre plano, por AppID y usuario**, independiente del disco |
| al renombrar el juego | se rompe, hay que arreglarlo (abajo) | **inmune**: el AppID no cambia |
| control fino | no | sí (elegir qué se sube, resolver conflictos) |

**Recomendación por defecto: Auto-Cloud**, porque mantiene las dos builds idénticas y el problema
del renombrado tiene solución (abajo). **La API es la salida** cuando el renombrado o el control
fino pesen más que la simplicidad — y encaja sin rediseño, porque no es más que otro `IStore`.

Para la API existen dos envoltorios libres y sanos: **Steamworks.NET** (MIT, orientado a Unity) y
**Facepunch.Steamworks** (MIT).

#### Auto-Cloud: sobrevivir a un renombrado

Steam admite **varias entradas de root path** para la misma app, y eso resuelve el caso: se
**mantiene la entrada vieja y se añade la nueva**. La secuencia encaja sola con la adopción de §10:

> Steam sincroniza hacia abajo al arrancar y restaura la carpeta vieja desde la nube → arranca el
> juego → la adopción local ve la carpeta vieja → copia a la nueva → Steam sube la nueva al salir.

Tras unas cuantas versiones se retira la entrada vieja. Durante la transición los archivos cuentan
dos veces en la cuota, que es un precio menor.

| SO | root de Steam | dónde escribe Unity |
|---|---|---|
| Windows | `WinAppDataLocalLow` | `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>` |
| macOS | `MacAppSupport` | `~/Library/Application Support/<Company>/<Product>` |
| Linux | `LinuxHome` | `~/.config/unity3d/<Company>/<Product>` |

Configuración: patrón **`*.etm`**, recursivo en `account/` y `slots/`, y **ninguna entrada para
`machine/`** — Steam avisa explícitamente de no sincronizar configuración específica de la
máquina, y con razón: el PC del salón y el portátil no tienen la misma GPU.

> **Ojo con Linux:** su ruta tiene otra forma (`.config/unity3d/`), así que esa entrada necesita
> un subdirectorio distinto al de Windows y macOS. Es el fallo clásico que hace que las partidas
> no sincronicen solo en Linux.
>
> **No cambiéis `Company Name` ni `Product Name`** una vez publicado. Mueven la carpeta,
> huérfanan las partidas existentes y rompen el patrón de la nube de golpe.

---

## 15. Cómo saber si el diseño es correcto

Tres pruebas objetivas:

1. **El juego 2 debe poder adoptarlo escribiendo solo su modelo de datos y su captura /
   restauración, sin tocar una línea del paquete.**
2. **Este juego no debe tener ni que enterarse de que las ranuras existen.**
3. **`Eternal.Cli` compila y pasa sus pruebas** sin referenciar Unity — o sea, otro motor puede
   construir su propia capa visual sobre la misma API.

Corolario: **no congelar la API pública hasta que exista el juego 2.** Marcarla como inestable, o
el contrato os atará a decisiones tomadas con un solo caso de uso a la vista.

---

## 16. Riesgo principal y orden de construcción

**Generalidad especulativa.** Un sistema diseñado para N juegos hipotéticos antes de haber
portado el segundo acaba con abstracciones que no encajan con el segundo de todas formas. La
mitigación no es diseñar menos: es **diseñar las costuras y mantener finas las implementaciones**.

Orden recomendado, para que este juego no pague la abstracción por adelantado:

1. `Eternal.Core` — contenedor, pipeline, `IStore` asíncrono, veredictos de carga.
2. Adaptador Newtonsoft + almacén de archivo. **Con esto este juego ya funciona.**
3. `Eternal.Tests` — ida y vuelta, dorados, manipulación, conformidad de almacenes.
4. Almacén de WebGL con el `.jslib`.
5. `Eternal.Tooling` + Save Browser / Inspector / Export.
6. Save Forge + Load into Play — *lo agradecerá QA antes de lo que parece*.
7. Ranuras y catálogo — *implementado, no usado aquí*.
8. `Eternal.Crypto` — *implementado, no usado aquí*.
9. `Eternal.Cli` — barato, y es la prueba de independencia del motor.
10. Adaptador binario — *solo cuando un juego lo pida*.

---

## Fuentes

- [Unity Issue Tracker — WebGL StreamWriter no dispara `syncfs` al escribir en `/idbfs`](https://issuetracker.unity3d.com/issues/webgl-streamwriter-not-triggering-syncfs-when-writing-a-file-to-slash-idbfs)
- [Unity Discussions — volcar datos a IndexedDB en WebGL](https://discussions.unity.com/t/webgl-flushing-data-to-indexdb/240698)
- [Newtonsoft.Json for Unity — arreglar AOT con `link.xml`](https://github.com/applejag/Newtonsoft.Json-for-Unity/wiki/Fix-AOT-using-link.xml)
- [Steam Cloud — documentación de Steamworks](https://partner.steamgames.com/doc/features/cloud)
- [ISteamRemoteStorage — espacio de nombres plano por AppID](https://partner.steamgames.com/doc/api/ISteamRemoteStorage)
- [`IDBFactory.databases()` — MDN, Baseline 2024](https://developer.mozilla.org/en-US/docs/Web/API/IDBFactory/databases)
- [rlabrecque/Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) · [Facepunch/Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks)
- [`Application.persistentDataPath` en macOS para Steam Auto-Cloud — Unity Discussions](https://discussions.unity.com/t/application-persistentdatapath-on-macos-for-steam-auto-cloud/846485)
- [Cysharp/MemoryPack — tolerancia de versión y soporte en Unity](https://github.com/Cysharp/MemoryPack)
