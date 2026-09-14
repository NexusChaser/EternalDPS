# EternalDPS — plan de implementación

Plan de trabajo derivado de [EternalDPS_SaveSystem_Architecture.md](EternalDPS_SaveSystem_Architecture.md).
Ese documento dice **qué** y **por qué**; este dice **en qué orden** y **cuándo está hecho**.

**Cómo se usa:** se marcan las casillas conforme se cierran. Los identificadores (`CORE-04`,
`WEB-02`…) son **estables**: si una tarea se cae, se tacha con motivo, **no se renumera**, para que
las referencias en commits y mensajes sigan valiendo.

---

## Progreso

| fase | bloque | tareas | estado |
|---|---|---|---|
| 0 | Andamiaje del paquete | 9 | ✅ 9/9 |
| 1 | Núcleo: contenedor y tubería | 27 | ✅ 27/27 |
| 2 | Serialización y almacén de archivo | 14 | ✅ 14/14 |
| 3 | **Integración en este juego** (A ya; B, C, D bloqueados por interfaz) | 19 | ⬜ |
| 4 | Pruebas | 17 | ⬜ |
| 5 | WebGL | 9 | ⬜ |
| 6 | Tooling sin motor | 13 | ⬜ |
| 7 | Ventanas de Unity | 11 | ⬜ |
| 8 | Ranuras y catálogo | 9 | ⬜ |
| 9 | Cifrado | 6 | ⬜ |
| 10 | CLI | 5 | ⬜ |
| 11 | Steam (**las dos vías**) | 11 | ⬜ |
| 12 | Binario | 6 | ⬜ |
| 13 | Cierre y publicación | 7 | ⬜ |

**163 tareas.** De las decisiones de diseño hay **5 cerradas y una abierta**: `D-06`, cómo sale un
jugador de una partida. Las 6 decisiones de implementación de `CORE-01..08`, confirmadas.

**Hitos:**
- **H1 — este juego ya persiste** → al terminar el **bloque A** de la fase 3. Es el primer punto
  con valor real, y no depende de ninguna interfaz nueva.
- **H2 — el formato está blindado** → al terminar la fase 4. A partir de aquí el formato es contrato.
- **H3 — utilizable por QA** → al terminar la fase 7.
- **H4 — reutilizable de verdad** → al terminar la fase 13.

---

## Decisiones

- [x] **D-01 — Repositorio propio desde el principio.** ~~¿Embebido o repo aparte?~~ **Resuelto:**
  repositorio de GitHub propio, consumido por UPM vía git URL desde el primer día, para poder
  reutilizarlo y actualizarlo en varios proyectos sin copiar carpetas.
  *Consecuencia:* `PKG-01` cambia, `FIN-02` deja de ser una extracción y pasa a ser una revisión,
  y **`D-03` se vuelve crítica**: en un repo público no puede viajar ninguna clave.

- [x] **D-02 — Sobrevivir a un cambio de nombre.** ~~Congelar `Company`/`Product` antes de
  publicar.~~ **Resuelto, y mejor:** en vez de solo congelarlos, el sistema **adopta guardados
  huérfanos**. La identidad del juego pasa a vivir **dentro del archivo** (`productId`, un GUID
  congelado de por vida), no en la ruta. Antes de declarar que algo no se puede migrar, se
  interrogan los metadatos y, si de verdad es portable, se migra.
  *Consecuencia:* bloque nuevo de tareas de adopción (`CORE-19/20`, `STO-05/06`, `GAME-09`,
  `TST-14`, `TOOL-09`, `EDIT-09/10`). **En WebGL no es posible** (`WEB-07`).

- [x] **D-03 — La clave no es una, es un conjunto rotable.** **Resuelto.** La objeción era
  correcta: una clave estable entre versiones es a la vez lo que hace migrables los guardados y lo
  que hace permanente una filtración. Se resuelve **versionando la clave**: un byte `keyId` en el
  preámbulo, el juego lleva la actual más las retiradas, **se escribe con la más nueva y se lee
  con la que el archivo declare**, y un archivo firmado con clave retirada **se refirma solo** en
  el siguiente guardado. Reglas: **por juego, nunca compartida**; **el paquete no trae ninguna**
  (repo público), la aporta el juego vía `IKeyProvider`.
  *Techo asumido:* la clave nueva también está en el binario, así que esto no para a alguien
  decidido — para que **una clave publicada siga sirviendo**, que es el caso realista.
  *Queda como tarea, no como decisión:* generar y custodiar el material de este juego (`GAME-10`).

- [x] **D-04 — Alcance del «retomar partida».** **Resuelto:** se restaura **el estado del
  tablero**; si se cerró dentro de un minijuego, se vuelve a ese minijuego pero **se reinicia
  desde cero**. No importa quién iba ganando dentro de él.
  *Consecuencia importante:* **no hay que serializar nada interno de los minijuegos**. El modelo
  solo necesita datos de tablero más «qué minijuego estaba en curso». Y el punto de guardado es
  **al entrar al minijuego, con el tablero tal como estaba antes de sus recompensas**, para que
  reiniciarlo no pueda repartir premios dos veces.

- [x] **D-05 — Progreso: no ahora, pero contemplado.** **Resuelto:** hoy no hay nada de progreso.
  Más adelante habrá cosméticos (skins o similar). No se crea el archivo vacío: el tipo `Progress`
  ya está en la matriz, el cargador trata «no existe» como «progreso nuevo», y añadirlo después es
  una subida de `schemaVersion` normal. Lo único que hay que garantizar hoy es que **los
  cosméticos serán de ámbito cuenta, no de ranura** — o borrar una partida borraría las skins.

---

## Fase 0 — Andamiaje del paquete

- [x] **PKG-01** Crear **repositorio propio de GitHub** para el paquete, con su `package.json`
      (nombre `com.nexuschaser.eternaldps`, versión `0.1.0`, displayName, unity mínima) en la raíz,
      y añadirlo a este proyecto por **git URL** en `manifest.json`. → cierra `D-01`
- [x] **PKG-02** Estructura de carpetas: `Runtime/Core`, `Runtime/Tooling`, `Runtime/Unity`,
      `Runtime/Serialization.Newtonsoft`, `Runtime/Crypto`, `Editor`, `Tests`.
- [x] **PKG-03** `Eternal.Core.asmdef` — **sin referencias a UnityEngine**, `noEngineReferences: true`.
- [x] **PKG-04** `Eternal.Tooling.asmdef` — igual, solo referencia a Core.
- [x] **PKG-05** `Eternal.Unity.asmdef` y `Eternal.Unity.Editor.asmdef` (este último `includePlatforms: Editor`).
- [x] **PKG-06** `Eternal.Serialization.Newtonsoft.asmdef` con **version define** sobre
      `com.unity.nuget.newtonsoft-json`, para que no compile si el paquete no está.
- [x] **PKG-07** `Eternal.Crypto.asmdef` aparte, para que un juego que no lo use no lo envíe.
- [x] **PKG-08** `README.md` del paquete + `CHANGELOG.md` + `LICENSE` + aviso de **API inestable**
      hasta el juego 2.
- [x] **PKG-09** Comprobar que Unity compila el proyecto sin errores y que el paquete se consume
      correctamente. **Verificado:** los 7 ensamblados compilan; `Eternal.Core` y `Eternal.Tooling`
      **no referencian el motor** (comprobado sobre los ensamblados cargados, no sobre el asmdef);
      el *version define* de Newtonsoft resuelve y su adaptador carga; `EternalPackage` devuelve
      `com.nexuschaser.eternaldps` / `0.1.0` / `.etm`; y las **3 pruebas pasan**. El paquete se
      clona e instala desde la git URL con `package.json` válido.
      *Nota:* las pruebas de un paquete solo compilan si el proyecto lo declara en `testables`;
      añadido al `manifest.json`. El manifest queda apuntando a `file:` para desarrollar las fases
      siguientes, como documenta el README.

> **Hecho cuando:** el proyecto compila, `Eternal.Core` aparece en el Package Manager instalado
> desde GitHub, y no referencia UnityEngine.

---

## Fase 1 — Núcleo: contenedor y tubería

### Tipos base

- [x] **CORE-01** `Scope` (Machine · Account · Slot), `RecordKind` (Settings · Progress · Session).
- [x] **CORE-02** `SlotId` — identificador **opaco y estable** (GUID), con `SlotId.Default`.
      Nombre y orden de visualización son datos aparte, nunca el id.
- [x] **CORE-03** `EternalKey = (Scope, RecordKind, RecordId, SlotId)` + composición de ruta con
      colapso de segmentos ausentes.
- [x] **CORE-04** `SaveProfile`: `Lifetime`, `Cloud`, `OnMigrationFailure`, `OnIntegrityFailure`,
      `Conflict`, `Backups`.
- [x] **CORE-05** `LoadResult` como resultado cerrado con los nueve veredictos de la §12 de la
      arquitectura. **No excepciones** para casos esperados.
- [x] **CORE-06** `IntegrityReport` (veredicto + qué región falló + tamaños leídos).

### Interfaces

- [x] **CORE-07** `ISerializer` (con `FormatId`), `IDocumentSerializer` (opcional, árbol editable),
      `IByteTransform` (con `TransformId` e inversa), `IClock`, `IEternalLog`.
- [x] **CORE-08** `IStore` **asíncrono** + `StoreCapabilities`
      (`AtomicReplace`, `List`, `Delete`, `RandomAccess`).

### Contenedor `.etm`

- [x] **CORE-09** `IKeyProvider`: el **juego** aporta las claves, el paquete **no trae ninguna**
      (repo público). El núcleo falla ruidosamente si se le pide firmar sin clave. Nunca una
      cadena literal — aparece haciendo `strings` al binario. → ver `CORE-21..25`
- [x] **CORE-10** Escritor del preámbulo: magia `ETM1`, `containerVersion`, flags, longitudes,
      `serializerId`, lista ordenada de `transformIds`.
- [x] **CORE-11** Bloque de metadatos: **`productId`**, `schemaVersion`, `savedAtUtc`,
      `appVersion`, `slotName`, `playtime`, offsets de miniatura, campos libres del juego.
- [x] **CORE-12** Escritura del cuerpo aplicando la cadena de transformaciones **en orden**.
- [x] **CORE-13** Firma `HMAC-SHA256` sobre **todo lo anterior, preámbulo incluido** (cierra la
      degradación descrita en §5).
- [x] **CORE-14** Lector: parsear preámbulo **sin aplicar ninguna transformación**, y leer
      metadatos sin tocar el cuerpo.
- [x] **CORE-15** Verificación **en flujo**, sin deserializar el cuerpo (alimenta `VerifyAsync`).
- [x] **CORE-16** Registros de `TransformId` y `SerializerId`, con los rangos reservados y la regla
      de **no reutilizar nunca un número**.
- [x] **CORE-17** Transformación `Deflate` (`0x01`).
- [x] **CORE-18** Fachada `EternalDataDriver`: `SaveAsync`, `LoadAsync`, `DeleteAsync`,
      `ExistsAsync`, `VerifyAsync`. Orquesta serializador + transformaciones + almacén.
- [x] **CORE-19** `ProductIdentity`: el `productId` actual (GUID **congelado de por vida**) más la
      lista de **identidades heredadas** aceptadas. → sale de `D-02`
- [x] **CORE-20** `CanAdopt(bytes)`: comprueba en orden magia → `containerVersion` → `productId` →
      camino de migración, **sin deserializar el cuerpo**, y devuelve el motivo cuando dice que no.
- [x] **CORE-21** Byte **`keyId`** en el preámbulo, en offset 13. Se reserva **aunque solo exista
      una clave**: añadirlo después sería subir la versión del contenedor. → sale de `D-03`
- [x] **CORE-22** Conjunto de claves: **escribir siempre con la más nueva, leer con la que declare
      el archivo**. El `IKeyProvider` entrega la actual más las retiradas.
- [x] **CORE-23** **Refirmado automático**: un archivo cargado con clave retirada se reescribe con
      la actual en el siguiente guardado. Silencioso, el jugador no hace nada.
- [x] **CORE-24** Política de retirada por clave: `Active` → `ReadOnly` → `Warn` → `Rejected`.
- [x] **CORE-25** Derivación con **HKDF** en el paquete; el material de entrada lo pone el juego.

### Consecuencias de las decisiones de `CORE-01..08`

Salen de la tabla de abajo. No son decisiones nuevas: son los cabos que dejaron abiertos.

- [x] **CORE-26** El driver **comprueba conflictos de clave al registrarlas** y falla ahí mismo.
      Dos claves con el mismo ámbito, ranura e id pero **distinto tipo** apuntan al mismo archivo,
      porque la ruta no codifica el tipo. Ya existe `EternalKey.ConflictsWith`; esto es lo que lo
      llama. Tiene que reventar en el **primer segundo de la primera ejecución**, no meses después
      con una partida pisada. → cierra la decisión **2**
- [x] **CORE-27** Aviso —**no** bloqueo— cuando una clave usa una combinación de ámbito y tipo
      fuera de la matriz de la §3 (`ScopeRules.IsMeaningful`). Una vez por clave, al registrarla.
      Guardar **progreso con ámbito de máquina** se acepta, pero se dice: es progreso que se
      evapora al cambiar de ordenador. → cierra la decisión **6**

> **Hecho cuando:** se puede escribir y volver a leer un `byte[]` con un `ISerializer` y un
> `IStore` falsos en memoria, el HMAC detecta un bit cambiado, y `CanAdopt` reconoce un archivo
> escrito bajo otro nombre de producto.
>
> ✅ **Las tres cosas, comprobadas:** ida y vuelta con dobles en memoria; un bit cambiado se detecta
> en las cuatro regiones del contenedor, preámbulo incluido; y un guardado escrito bajo otra
> identidad de producto se reconoce como propio al declararla como heredada. **150 pruebas en
> verde**, y `Eternal.Core` sigue sin referenciar el motor.

### Decisiones tomadas al implementar `CORE-01..08`

Seis cosas que el plan no decía y hubo que decidir. **Las seis quedan confirmadas**; la columna
de veredicto dice qué se eligió y qué tarea cierra lo que faltaba. Se anotan aquí para que no haya
que volver a razonarlas dentro de tres fases, cuando ya sean contrato.

| decisión | por qué | reversible | **veredicto** |
|---|---|---|---|
| **`RecordId` se pasa a minúsculas** y solo admite `[a-z0-9_-]` empezando por letra o dígito | Windows no distingue mayúsculas y Linux sí: `MySave` y `mysave` serían un archivo en una plataforma y dos en la otra. Plegar la caja lo hace un archivo en todas. El alfabeto estrecho cierra además el recorrido de rutas (`../`), los puntos y espacios finales, y las diferencias de normalización Unicode | sí, hasta que se publique | **Se queda.** Es la intersección real de NTFS, ext4, APFS/HFS+, FAT32, IndexedDB y consola — no una preferencia. El caso que de verdad muerde es macOS: HFS+ normaliza los nombres a NFD, así que una `é` se escribe de una forma y se busca de otra; ser ASCII puro lo elimina de raíz en vez de mitigarlo. **No limita al jugador**: el nombre visible de la ranura vive en los metadatos y no tiene ninguna de estas restricciones |
| **El `RecordKind` no aparece en la ruta** | Es lo que dice la §9 de la arquitectura: `account/prefs.etm` y `account/progress.etm` se distinguen por el id, no por el tipo. El tipo elige la **política**, no el sitio. Consecuencia: el id debe ser único dentro del ámbito aunque cambie el tipo, y `EternalKey.ConflictsWith` es lo que lo detecta | sí | **Se queda la ruta, pero faltaba el cable.** La §9 es contrato y moverla después es una migración. `ConflictsWith` detecta el choque pero **nadie lo llamaba**: era una alarma sin conectar. Lo conecta `CORE-26`. Se descartó meter el tipo en la ruta: contradice la §9 y no gana nada que la comprobación no dé |
| **`SlotId` se colapsa a `Default` cuando el ámbito no es `Slot`** | Si no, dos claves que apuntan al mismo archivo compararían distinto. Es el bug silencioso de toda caché con clave compuesta | sí | **Se queda.** La alternativa era lanzar excepción al pasar una ranura a un ámbito que no la usa; se descartó porque rompe cualquier bucle que recorra ámbitos con la misma ranura sin que nadie haya hecho nada mal |
| **`EternalNode`**, un árbol propio para `IDocumentSerializer` | Sin un tipo concreto la interfaz no se puede llamar y la tarea sería vacía. Devolver `object` sería peor. Los números guardan su **texto original**: pasar un id de 64 bits por un `double` lo redondea y lo reescribe distinto — abrir un guardado en una herramienta y cerrarlo sin tocar nada tiene que dar los mismos bytes. **Pendiente:** no conserva el orden de los miembros; revisar en la fase 6 | sí, nadie lo consume aún | **Se queda, con la deuda anotada en `TOOL-13`.** Guardar el texto original de los números es lo que está bien decidido y no se toca. El orden se deja para la fase 6 **a propósito**: aún no se sabe si las ventanas editarán un árbol o un texto, y adelantarse podía ser trabajo tirado. Nadie lo consume, así que esperar cuesta cero |
| **`IStore.FlushAsync`** añadido a la interfaz | La §14 exige forzar `FS.syncfs()` en WebGL. Sin un método en el contrato, el driver no tiene dónde llamarlo y el volcado se queda fuera de la abstracción | no, es la interfaz | **Se queda, y era la que había que acertar hoy.** Volcar dentro de `WriteAsync` sería un error de rendimiento serio: `FS.syncfs()` no vuelca un archivo, vuelca el sistema de archivos **entero**, así que guardar ajustes + sesión + progreso dispararía tres volcados completos en vez de uno. Una interfaz opcional `IFlushableStore` sería peor: obliga a un *cast* en cada guardado, justo en la ruta que solo corre en WebGL. El riesgo de que alguien escriba y no vuelque lo cubre el driver, que llama a flush siempre al cerrar un guardado |
| **`ScopeRules` es informativo, no bloquea** | La matriz de la §3 tiene seis casillas con sentido, pero un juego puede tener un motivo que la matriz no previó. El sistema lo señala y se aparta | sí | **Se queda sin bloquear, pero deja de estar callado.** Tal como estaba no decía *nada*: guardar progreso con ámbito de máquina se aceptaba en silencio y el jugador lo descubría al estrenar portátil. `CORE-27` lo convierte en un aviso |

**Cubierto por 62 pruebas nuevas** (65 en total con las tres de la fase 0), todas en verde, y
`Eternal.Core` sigue sin referenciar el motor: solo `netstandard`.

### Decisiones tomadas al implementar `CORE-09..21`

| decisión | por qué | reversible | veredicto |
|---|---|---|---|
| **Los metadatos usan una codificación binaria fija del núcleo, no el `ISerializer` enchufable** | Son lo que responde «¿este archivo es mío?». Un build tiene que poder responderlo sobre un archivo escrito por otro build cuyo serializador no lleva compilado; si dependieran del serializador, un módulo opcional ausente dejaría el guardado ilegible **y** no identificable, así que ni listarlo ni borrarlo. Rompe el `CanAdopt` de la §10 | **no**, es el formato | **Se queda.** La §5 decía «serializados, comprimidos»: corregido ahí |
| **Los metadatos no se comprimen** | Son unos cientos de bytes. Comprimirlos no ahorra nada que justifique inflarlos en cada entrada de la lista de ranuras, y lo único grande —la miniatura— ya llega comprimida | no, es el formato | **Se queda** |
| **La miniatura vive dentro del bloque de metadatos** | Tiene que leerse sin tocar el cuerpo, como el resto del bloque. **Consecuencia:** `metaLen` son 2 bytes, así que el bloque no pasa de 64 KB y ese es el techo real de la miniatura | no, es el formato | **Se queda**, con el límite documentado y con un error claro al superarlo |
| **Los GUID se escriben en orden RFC 4122**, no en el de `Guid.ToByteArray` | .NET invierte los tres primeros campos. Dentro de .NET da igual porque va y vuelve, pero una herramienta escrita en otro lenguaje leería un `productId` distinto del que dice su forma de texto | no, es el formato | **Se queda.** Cubierto por una prueba de bytes concretos |
| **`ReadMetadata` no verifica la firma** | Una partida dañada tiene que poder dibujarse en la lista para que el jugador la vea y la borre. Negarse a leer su nombre porque el cuerpo está roto la hace desaparecer de la interfaz y deja al jugador atascado | sí | **Se queda.** Todo lo que actúa sobre el contenido sí verifica antes |
| **`CanAdopt` no comprueba la firma** | Un archivo de la carpeta anterior del propio juego está firmado con la misma clave y se verificará al cargar. Exigir firma en esta etapa haría que una rotación de clave volviese no adoptables los guardados viejos, que es exactamente lo que la rotación existe para evitar | sí | **Se queda** |
| **Dos veredictos nuevos en `IntegrityStatus`**: `UnsupportedVersion` y `RejectedKey` | «De una versión más nueva» no es daño y no puede llegar como `IntegrityFailed`, o un build se sentiría con derecho a sobrescribir un guardado del futuro. `RejectedKey` distingue «firma rota» de «firma intacta con clave ya no aceptada» | sí | **Se queda.** El mapeo veredicto→`LoadStatus` está en un solo sitio para que no se desincronicen |

**Cubierto por 54 pruebas nuevas** (119 en total), todas en verde. Incluye la prueba que la §5
exige explícitamente: **quitar una transformación de la cabecera no pasa la firma**.

### Decisiones tomadas al implementar `CORE-22..27`

| decisión | por qué | reversible | veredicto |
|---|---|---|---|
| **`EternalKeyRing` impone tres invariantes**: ningún id repetido, exactamente una clave activa, y la activa es la de id más alto | Dos claves activas harían que cuál firma dependa del orden en que se pasaron — de los que funcionan en una máquina y no en otra. Que la rotación solo avance es lo que permite a `TOOL-11` tomar «el siguiente id libre» sin ambigüedad | sí | **Se queda.** Todo se comprueba al construir, así que un anillo mal montado falla al arrancar y no cuando a un jugador no le carga la partida |
| **`TryResign` verifica antes de refirmar** | Sin eso, refirmar cogería un archivo que alguien editó y lo devolvería correctamente firmado con la clave actual: **blanquearía exactamente lo que la firma existe para detectar**. Es el error que convierte la rotación en un agujero | **no**, es de seguridad | **Se queda.** Con prueba dedicada |
| **`AutoResign` activado por defecto**: se refirma al **leer**, no solo al guardar | El plan decía «en el siguiente guardado», que es gratis porque todo guardado usa ya la clave actual. Pero eso deja la clave filtrada sirviendo contra todo lo que el jugador solo lee — progreso de cuenta que cambia una vez al mes. Refirmar al leer lo cierra en el siguiente arranque | sí, es una opción | **Se queda encendido**, y se puede apagar. Es **más** de lo que pedía el plan, dicho aquí para que se pueda discutir |
| **Un fallo al refirmar nunca hace fallar la carga** | Refirmar es mantenimiento. Un registro que cargó bien no puede volver como error porque la limpieza posterior no funcionase | sí | **Se queda**, con aviso en el log |
| **`IntegrityReport.SigningKeyState`** nuevo | Sin él, `Warn` se comporta idéntico a `ReadOnly` y la etapa no significa nada. Ahora el informe dice con qué estado de clave verificó, y el driver avisa | sí | **Se queda** |
| **La comprobación de conflictos también corre en caliente**, no solo sobre los registros declarados | Declararlos por adelantado solo adelanta el fallo al arranque. Si no se declaran, el choque se detecta igual la primera vez que se usa cada registro — un diccionario y un candado, coste nulo | sí | **Se queda** |
| **El aviso de la matriz sale una vez por registro**, no en cada llamada | Un aviso que se repite en cada guardado es un aviso que nadie lee | sí | **Se queda** |

**HKDF está comprobado contra los vectores del Apéndice A del RFC 5869** —los tres casos, incluido
el de entradas largas que obliga a encadenar tres bloques—. Una primitiva criptográfica escrita a
mano que nadie contrastó con la especificación es peor que no tenerla: parece que funciona.

**Cubierto por 31 pruebas nuevas** (150 en total), todas en verde.

---

## Fase 2 — Serialización y almacén de archivo

- [x] **SER-01** Adaptador Newtonsoft, `FormatId 0x01`, implementando también `IDocumentSerializer`.
- [x] **SER-02** Ajustes de Json.NET: cultura invariante, **`TypeNameHandling` desactivado**
      (agujero de deserialización conocido), fechas en UTC ISO-8601.
- [x] **SER-03** Patrón de polimorfismo con `JsonConverter` propio y **campo discriminador
      explícito**, como alternativa segura a `TypeNameHandling`.
- [x] **STO-01** `FileStore` asíncrono con todas las capacidades.
- [x] **STO-02** Escritura atómica: `.part` → reemplazo. La extensión `.part` es deliberada para que
      el patrón `*.etm` de Steam **no** la recoja.
- [x] **STO-03** Rotación de copias `.bak` según `SaveProfile.Backups`.
- [x] **STO-04** Listado y borrado, con claves inexistentes tratadas como caso normal.
- [x] **STO-05** Barrido de **carpetas hermanas** bajo la raíz de datos, buscando `.etm` adoptables
      con `CORE-20`. Acotado a hermanas: nunca se recorre el disco entero.
- [x] **STO-06** Adopción automática al primer arranque si la carpeta actual está vacía:
      **copiar, nunca mover**, escribir marca para no repetirlo, quedarse con la candidata de
      `savedAtUtc` más reciente si hay varias, y dejar constancia en el log.
- [x] **UNI-01** `Eternal.Unity`: resolución de rutas sobre `Application.persistentDataPath` con la
      disposición `machine/` · `account/` · `slots/`.
- [x] **UNI-02** Arranque del sistema y registro de serializadores y transformaciones disponibles.
- [x] **UNI-03** Disparadores de guardado: `OnApplicationPause(true)` y `OnApplicationFocus(false)`.
      **Nunca `OnApplicationQuit`** — en Android y WebGL no se llama de forma fiable.
- [x] **UNI-04** Antirrebote, para no escribir en cada movimiento de un slider.
- [x] **UNI-05** `link.xml` en el paquete preservando `JsonConvert` y `DefaultContractResolver`.

> **Hecho cuando:** un modelo de prueba se guarda en disco desde el editor, se relee tras reiniciar
> Unity, y el archivo es binario ilegible en un editor de texto.
>
> 🔄 **Parcial:** ya hay una prueba que monta el driver sobre una carpeta real del disco, guarda,
> relee y comprueba que el archivo resultante **no contiene el texto que se guardó**. Falta la parte
> del editor, que depende de `UNI-01..05`.

### Decisiones tomadas al implementar `SER-01..03` y `STO-01..06`

| decisión | por qué | reversible | veredicto |
|---|---|---|---|
| **`FileStore` vive en `Eternal.Core`, no en `Eternal.Unity`** | No necesita nada más que `System.IO`. En el núcleo lo usan las herramientas de línea de comandos y lo prueba CI sin abrir Unity, que es la propiedad alrededor de la que está montado el paquete. `Eternal.Unity` aporta **solo la ruta raíz** | sí | **Se queda.** La tabla de la §7 decía «almacenes» en `Eternal.Unity`: corregida ahí |
| **La rotación de copias está en el driver, no en el almacén** | El plan la puso bajo `STO`, lo que sugería el almacén de archivos. Hecha con las operaciones del `IStore`, **todos** los backends tienen copias: el navegador y Steam Cloud incluidos, donde no hay `rename` en el que apoyarse | sí | **Se queda** |
| **Antes de promover un registro a copia, se verifica** | Sin esa comprobación, un guardado que se hubiera corrompido se copiaría encima de la última copia buena, y el siguiente guardado lo empujaría por la cadena hasta que **todas** las copias fueran el mismo archivo dañado. El único momento para el que existen las copias sería el momento en que todas se habrían sobrescrito | **no**, es lo que las hace útiles | **Se queda.** Con prueba dedicada |
| **`File.Move(origen, destino, overwrite)` no existe** en el perfil netstandard de Unity | Descubierto compilando, no suponiendo. Se usa `File.Replace`, que el sistema operativo hace en un solo paso. Donde no está soportado —almacenamiento externo de Android en FAT32, por ejemplo— **degrada a borrar y renombrar, y ahí sí hay una ventana sin atomicidad**. Es exactamente por esto que la atomicidad se declara como capacidad y no se promete | no, es la API disponible | **Se queda**, con el límite escrito en el código |
| **`.part` para el archivo temporal**, nunca `.etm` | Auto-Cloud de Steam se configura con un patrón, y el patrón obvio es `*.etm`. Un temporal a medio escribir que encajara se subiría a la nube como si fuera una partida | no | **Se queda.** Los `.part` además nunca se listan como registros |
| **Un `DateTimeOffset` no se normaliza a UTC**; un `DateTime` sí | `DateTime` es el ambiguo: no lleva desplazamiento, así que el mismo texto significa un instante distinto según dónde se lea. `DateTimeOffset` ya lo lleva. **Consecuencia:** dos guardados escritos en husos distintos muestran texto distinto para el mismo instante, así que se comparan como instantes y jamás como cadenas | sí | **Se queda.** Yo había documentado «las fechas van en UTC» sin matizar, y no era exacto: corregido |
| **El barrido de adopción no deja marca si no encontró nada** | Así vuelve a intentarlo en el siguiente arranque, por si el jugador restaura después una carpeta antigua. Listar cuatro carpetas hermanas no cuesta nada | sí | **Se queda** |
| **`overrideReferences` explícito en los asmdef** que usan Newtonsoft | Estaban compilando solo porque Unity auto-referencia **todas** las DLL del proyecto. Declarar `Newtonsoft.Json.dll` es lo que la referencia realmente es, y evita que el ensamblado arrastre en silencio DLL ajenas | sí | **Se queda** |

**Cubierto por 56 pruebas nuevas** (206 en total), todas en verde. Incluye el intento real de colar
un `$type` en un guardado, no solo comprobar el ajuste.

### Decisiones tomadas al implementar `UNI-01..05`

| decisión | por qué | reversible | veredicto |
|---|---|---|---|
| **El antirrebote vive en `Eternal.Core`, no en la capa de Unity** | Cuenta segundos que le pasan desde fuera en vez de leer un reloj o un frame. Así la temporización se prueba **exacta** sin juego corriendo, y otro motor solo tiene que darle su delta | sí | **Se queda.** La capa de Unity solo lo alimenta con `Time.unscaledDeltaTime` |
| **Se usa `unscaledDeltaTime`, no `deltaTime`** | Pausar el juego poniendo la escala de tiempo a cero también dejaría de escribir guardados para siempre | sí | **Se queda** |
| **El valor se lee al escribir, no al marcar** | Es lo que hace correcto el antirrebote: un slider arrastrado por cincuenta valores escribe el quincuagésimo una vez, en vez del primero tarde | sí | **Se queda** |
| **Dos escrituras del mismo registro nunca corren a la vez** | Habría dos escritores compitiendo por el mismo archivo y cuál aterrizara último sería suerte. Si llega un cambio con una escritura en vuelo, se vuelve a marcar sucio | sí | **Se queda** |
| **El arranque *falla* en WebGL en vez de avisar** | Un almacén de archivos ahí aparenta funcionar y lo pierde todo al cerrar la pestaña, porque Unity no vuelca el sistema de archivos virtual a IndexedDB por su cuenta. Es el peor fallo posible: silencioso y solo en producción. Fallar al arrancar es lo único que no llega a un jugador | sí, hasta la fase 5 | **Se queda.** Se puede saltar pasando `Store` a mano |
| **Sobrecarga no genérica `SaveAsync(key, value, Type, …)`** | El runner conoce el tipo en tiempo de ejecución, no en compilación. La genérica delega en ella | sí | **Se queda** |
| **El `link.xml` es deliberadamente estrecho** | Nombrar el ensamblado entero de Newtonsoft conservaría todo y engordaría la build sin motivo. **Un juego tiene que preservar sus propios modelos de guardado en su propio `link.xml`**: el paquete no puede saber cuáles son | sí | **Se queda**, con esa advertencia escrita en el archivo |

**Cubierto por 27 pruebas nuevas** (233 en total), todas en verde.

> **Un defecto que encontraron las pruebas, no la revisión.** `EternalSaveRunner.Create` llamaba a
> `DontDestroyOnLoad` sin condición. Eso es un **error directo fuera del modo de juego**, así que el
> componente era inusable desde cualquier herramienta de editor que quisiera escribir un guardado — y
> la fase 7 está llena de ellas. En un juego publicado no se habría notado nunca, porque ahí siempre
> se está en modo de juego: exactamente el tipo de fallo que solo aparece cuando alguien más intenta
> usar el paquete. Ahora la llamada está condicionada a `Application.isPlaying`, con una prueba que
> lo fija.
>
> De paso cambié `HideFlags.HideAndDontSave` por `HideFlags.DontSave`: el objeto sigue sin
> guardarse en la escena, pero ahora **se ve en la jerarquía**. Quien busque por qué un guardado
> ocurrió o no debería poder encontrar al responsable.

---

## Fase 3 — Integración en este juego

Dividida en bloques porque **no todo depende de nosotros**. Lo que ya tiene interfaz se puede
hacer hoy; lo que no la tiene está bloqueado por decisiones de diseño del juego, no por el sistema
de guardado. Mezclarlo todo en una lista haría parecer pendiente de código algo que está pendiente
de decidir.

> **Sobre el estado actual, dicho con precisión.** Hoy el juego no persiste nada en build, y eso
> **no es un fallo**: `CycloneAMS` se hizo agnóstico a propósito, y para una jam persistir entre
> sesiones no aporta nada — basta con que los valores vivan mientras el juego está abierto. Es
> funcionalidad no implementada, no un defecto. Lo que cambia ahora es que esta ya es la versión
> final.

---

### Bloque A — Ajustes que ya tienen interfaz · **Hito H1**

Los cuatro sliders de volumen y el selector de idioma **ya existen en `MainMenu.unity` y ya están
conectados**. Persistirlos no añade un solo elemento de interfaz: el slider sigue escribiendo en
`CycloneMemory` igual que hoy, y lo único nuevo es que ese valor se vuelque a disco y se lea al
arrancar. Por eso este bloque no depende de nada.

Su valor real no es el volumen: es **comprobar la fontanería entera en una build de PC** —
IL2CPP, rutas, firma, arranque— con cuatro floats en vez de con el estado de una partida.

- [x] **GAME-01** Definir los registros de este bloque: **`account/prefs`** (volúmenes + idioma),
      ámbito cuenta. El registro `machine/display` se define en el bloque C, con los gráficos.
      Progreso no se crea: el cargador trata «no existe» como «nuevo». Los cosméticos futuros irán
      en **ámbito cuenta**, nunca en ranura — o borrar una partida borraría las skins.
      → cierra `D-05`
- [x] **GAME-03** Asmdef propio para los modelos de datos, para preservarlo entero en `link.xml`
      en vez de ir tipo por tipo.
- [x] **GAME-04** Persistir los **cuatro volúmenes** y enlazarlos con `CycloneMemory` al arrancar.
      Antirrebote incluido: un slider arrastrado escribe una vez, no sesenta.
- [x] **GAME-05** Persistir el **idioma** y aplicarlo antes de que se resuelva la primera cadena
      localizada.
- [ ] **GAME-08** Verificar en una **build real de PC**, no solo en el editor, que volumen e idioma
      sobreviven al cierre. Es el punto donde aparecen los problemas de IL2CPP y de rutas, y por eso
      va aquí y no al final.
- [x] **GAME-09** `productId` congelado y adopción automática de `STO-06` enganchada al arranque.
- [x] **GAME-10** Clave `keyId 1` e `IKeyProvider` del juego en su propio ensamblado
      (`CarloVsCarlo.SaveKeys`, sin referencias al motor), con custodia en el repo privado del
      juego, verificado como `PRIVATE`.

> **Hecho cuando:** cierras la build de PC, la vuelves a abrir, y el volumen y el idioma siguen
> donde los dejaste.
>
> 🔄 **6 de 7.** Verificado de punta a punta contra el disco real, con la clave del juego: el
> registro se escribe (190 bytes), **no es legible como texto**, la firma es válida, el `productId`
> es el correcto, y tras volver a arrancar los cuatro volúmenes y el idioma vuelven exactos. Un byte
> cambiado se detecta, y con el archivo roto el juego **arranca igual** y usa los valores por
> defecto.
>
> ⚠️ **Lo único sin verificar: que los volúmenes lleguen al mixer.** `AudioMixer.SetFloat` no acepta
> valores fuera del modo de juego, así que eso solo se puede comprobar jugando. Es exactamente lo
> que cubre `GAME-08`.

### Decisiones tomadas al implementar el bloque A

| decisión | por qué |
|---|---|
| **No se toca ni una línea de CycloneAMS** | Es un sistema agnóstico y reutilizable, y esa es justo la propiedad que lo hace valioso para el siguiente proyecto. La persistencia se apoya encima usando su API pública; CycloneAMS no se entera de que existe |
| **Se detectan los cambios por sondeo**, no por evento | CycloneAMS no expone ningún evento de cambio de volumen, y añadírselo sería acoplarlo. Comparar cuatro `float` y una cadena por frame no cuesta nada al lado de dibujar un frame, y deja el acoplamiento en una sola dirección |
| **Los volúmenes son cuatro campos**, no un mapa por `ChannelType` | Ese enum tiene un hueco deliberado (`UI = 4`, con el 3 vacío para que un valor retirado no se reutilice). Meter el enum en un guardado ataría el formato a esos números para siempre |
| **El idioma se guarda por código (`es`), no por índice** | Un índice apuntaría a otro idioma el día que se añada un locale al proyecto |
| **La configuración vive en `Resources`** | El idioma hay que aplicarlo antes de que se resuelva la primera cadena, y a esa altura no hay ninguna escena cargada de la que leer una referencia. Un componente en la primera escena se rompería en cuanto alguien arranque desde otra, que es lo que se hace todo el día trabajando |
| **La lectura al arrancar bloquea** | Unos cientos de bytes de un disco local, una vez. Lo que compra es el orden: el idioma puesto antes de la primera cadena. En navegador no valdría, y ahí hará falta otra respuesta |
| **Un fallo del audio no tumba el arranque** | `SetVolume` lanza excepción si el mixer rechaza el valor. Lo descubrí probando: sin protección, un problema con el **volumen** habría hecho perder también el **idioma**. Ahora avisa y sigue |

---

### Bloque B — Partida en curso · **bloqueado por interfaz**

Aquí está el valor de verdad para el jugador, y **el modelo de datos ya existe**:
`Board_MatchSession_SO` ya tiene `Capture()` y `Restore()`, campos planos, y lo usan
`Board_GameManager` y `Board_RoundManager`. Lo que falta no es el estado, es que sobreviva al
cierre del proceso.

**Pero no se puede empezar**, y no por el código. El juego **no tiene pausa**: cero acción `Pause`
en `Game.inputactions`, y los únicos usos de `Time.timeScale` están en el fader de carga y en los
tweens de ventanas. Sin pausa no hay dónde poner «salir de la partida», y sin eso no hay momento
en el que preguntar qué se hace con el guardado.

- [ ] **D-06 — Cómo sale un jugador de una partida.** *Abierta.* Cuatro preguntas, y son de
      diseño del juego, no del sistema de guardado:
      **(a)** ¿Hay pausa? ¿Con qué tecla, y en qué mapas — solo en el tablero, o también dentro de
      los minijuegos?
      **(b)** ¿Se puede abandonar una partida a medias, o el único modo de salir es cerrar el juego?
      **(c)** Al salir, ¿guarda y sale, pregunta, o descarta?
      **(d)** ¿«Continuar» aparece aparte de «Jugar» en el menú? ¿Y si se pulsa «Jugar» con una
      partida guardada: se pisa sin avisar, o se pregunta?
      *Consecuencia de (a) que hay que decidir a sabiendas:* como `D-04` fija que **lo interno de un
      minijuego no se guarda y al reanudar se reinicia**, permitir pausar *dentro* de un minijuego y
      salir significa que el jugador vuelve al principio de ese minijuego, no a donde estaba.

**Prerrequisitos de interfaz.** No son tareas del sistema de guardado; son del juego, y sin ellas
lo de abajo no tiene dónde engancharse.

- [ ] **GAME-12** Acción `Pause` en `Game.inputactions` y sistema de pausa (teclado y mando).
- [ ] **GAME-13** Menú de pausa.
- [ ] **GAME-14** Salir de la partida desde la pausa, con el flujo que decida `D-06(c)`.
- [ ] **GAME-15** Botón **«Continuar»** en el menú principal, visible solo si existe una partida
      guardada y esa partida **verifica**. Si está dañada, no se ofrece como si estuviera bien.
- [ ] **GAME-16** Qué ocurre al pulsar «Jugar» con una partida guardada, según `D-06(d)`.

**Del sistema de guardado**, una vez exista lo anterior:

- [ ] **GAME-02** Modelo de estado de partida como **POCO plano**, espejo de
      `Board_MatchSession_SO`: turno, posiciones, puntuaciones, inventarios, **minijuego en curso**
      y semilla del RNG. Un `ScriptableObject` no se serializa limpio ni debe llevar referencias a
      objetos de Unity dentro de un guardado. `Capture`/`Restore` ya definen qué entra y qué sale,
      así que el espejo es mecánico. Con `schemaVersion` desde el primer commit. → cierra `D-04`
- [ ] **GAME-07** Captura y restauración. El punto de guardado es **al entrar al minijuego, con el
      tablero tal como estaba antes de sus recompensas** — así reiniciar el minijuego no puede
      repartir premios dos veces.
- [ ] **GAME-17** Borrar la partida al terminarla. El perfil es `Lifetime.Session`: existe para
      reanudarse una vez, y dejarla ahí permitiría rebobinar una partida ya jugada.
- [ ] **GAME-19** Qué se le enseña al jugador si la partida no carga. El veredicto ya viene del
      sistema (`IntegrityFailed`, `SchemaTooNew`…) y la política del perfil ya decide qué hacer;
      falta el texto y la pantalla. **Nunca en silencio**, y nunca acusando al jugador.

> **Hecho cuando:** empiezas una partida, entras a un minijuego, cierras el juego, lo reabres y
> continúas desde el tablero — con el minijuego reiniciado, no a medias.

---

### Bloque C — Gráficos · **bloqueado por interfaz**

No existe nada: cero usos de `QualitySettings` o `Screen.SetResolution` en todo el proyecto. Esto
no es persistencia pendiente, es una función del juego que aún no se ha construido.

- [ ] **GAME-18** Menú de calidad gráfica y resolución. *Prerrequisito, no es del sistema de
      guardado.*
- [ ] **GAME-06** Definir el registro **`machine/display`** y persistir calidad y resolución en él.
      Ámbito de máquina, **nunca a la nube**: enviar la resolución de este ordenador al portátil del
      jugador es un fallo, no una función. Se aplica **antes del primer frame**, lo que condiciona
      dónde vive el arranque del sistema.

> **Hecho cuando:** cambias la calidad, cierras la build, la reabres y sigue como la dejaste — y al
> abrirla en otra máquina, esa otra máquina conserva la suya.

---

### Bloque D — Portabilidad · *cuando haga falta*

- [ ] **GAME-11** Exportar e importar guardado desde el juego: a archivo y a cadena pegable. Es la
      única vía entre orígenes distintos y entre plataformas distintas. Necesita su propia pantalla,
      así que también depende de interfaz.

---

## Fase 4 — Pruebas · **Hito H2**

Corre en CI **sin abrir Unity**, que es el beneficio concreto de haber mantenido el núcleo limpio.

- [ ] **TST-01** Proyecto de pruebas sobre `Eternal.Core` y `Eternal.Tooling`, ejecutable con
      `dotnet test`.
- [ ] **TST-02** Ida y vuelta: para cada serializador × cada cadena de transformaciones,
      `leer(escribir(x)) == x`.
- [ ] **TST-03** Generar y **commitear los archivos dorados** de la versión 1 del contenedor.
- [ ] **TST-04** Prueba de dorados: los `.etm` de la versión N cargan en la N+1. **Los dorados
      viejos no se tocan jamás**; cada cambio de contenedor **añade** uno nuevo.
- [ ] **TST-05** Manipulación: voltear un bit en el preámbulo → veredicto correcto.
- [ ] **TST-06** Manipulación: en los metadatos.
- [ ] **TST-07** Manipulación: en el cuerpo.
- [ ] **TST-08** Manipulación: en la propia firma.
- [ ] **TST-09** **Degradación**: editar la cadena de transformaciones para declarar «sin cifrado»
      y comprobar que la integridad falla. Es el ataque de §5 convertido en test.
- [ ] **TST-10** Truncamiento en N posiciones: ni excepción sin controlar ni cuelgue.
- [ ] **TST-11** **Suite de conformidad de `IStore`**: escribir, leer, sobrescribir, borrar,
      listar, clave inexistente, caracteres raros, concurrencia. Todo backend futuro la pasa.
- [ ] **TST-12** Decorador de `IStore` que **falla en la escritura N**, para verificar que tras una
      muerte a media escritura queda el archivo viejo o el nuevo, **nunca uno a medias**.
- [ ] **TST-13** Banco de migraciones: un fixture por cada `schemaVersion` histórica.
- [ ] **TST-14** **Adopción**: un `.etm` escrito con otro `Company`/`Product` pero el mismo
      `productId` se adopta; uno con `productId` ajeno se rechaza **con motivo**; uno con
      `schemaVersion` sin camino de migración se rechaza sin tocar el archivo original.
- [ ] **TST-15** **Rotación de claves**: un archivo con `keyId 1` se lee en un build que ya escribe
      con `keyId 2`; al volver a guardar sale con `keyId 2`; una clave en `Rejected` se rechaza; un
      `keyId` desconocido da veredicto claro y no una excepción.
- [ ] **TST-16** Pasar la suite de conformidad `TST-11` contra `SteamRemoteStorageStore`.
- [ ] **TST-17** **Generador de claves**: dos invocaciones dan material distinto; el `keyId` avanza
      y **nunca se reutiliza**; sobrescribir uno existente falla; el conjunto emitido se lee.

> **Hecho cuando:** `dotnet test` pasa en verde en CI sin Unity instalado.

---

## Fase 5 — WebGL

- [ ] **WEB-01** Plugin `.jslib` en `Assets/Plugins/WebGL/` que exponga `FS.syncfs(false, cb)`.
- [ ] **WEB-02** `WebGLStore` que llama al volcado tras cada escritura y **no da el guardado por
      hecho hasta que vuelve el callback**.
- [ ] **WEB-03** Declarar el almacén **sin `AtomicReplace`** y aplicar la estrategia alternativa:
      copia previa → escribir → `syncfs` → verificar.
- [ ] **WEB-04** Pasar la suite de conformidad `TST-11` contra `WebGLStore`.
- [ ] **WEB-05** Prueba manual en build WebGL real: guardar, **cerrar la pestaña**, reabrir y
      comprobar que el dato está.
- [ ] **WEB-06** Comprobar el comportamiento con IndexedDB bloqueado o en ventana privada:
      degradar con aviso, no reventar.
- [ ] **WEB-07** **Adopción en WebGL** vía `indexedDB.databases()` desde el `.jslib`: enumerar las
      bases del origen, localizar el IDBFS antiguo, leer las entradas y pasarlas a `CanAdopt`.
      Es Baseline desde mayo de 2024 y su uso previsto es exactamente este.
- [ ] **WEB-08** Detección de característica: si el navegador no soporta `databases()`, saltarse la
      adopción **sin romperse**.
- [ ] **WEB-09** Documentar que **cruzar orígenes es imposible** (frontera del navegador, no de
      Unity) y que ese caso se cubre con el exportar/importar de `GAME-11`.

> **Hecho cuando:** el dato sobrevive a cerrar la pestaña en una build WebGL servida de verdad.

---

## Fase 6 — Tooling sin motor

- [ ] **TOOL-01** `EternalDocument`: abrir desde bytes, exponer preámbulo, metadatos, veredicto y
      cuerpo.
- [ ] **TOOL-02** Metadatos legibles **aunque el cuerpo esté corrupto** — requisito para que una
      partida dañada no desaparezca de la lista.
- [ ] **TOOL-03** Cuerpo como árbol editable cuando el serializador implementa `IDocumentSerializer`.
- [ ] **TOOL-04** **Degradación a edición tipada** cuando no lo implementa (el caso del binario).
- [ ] **TOOL-05** `RepackAsync`: recalcular longitudes y **refirmar**.
- [ ] **TOOL-06** `EternalDocument.Create(modelo, perfil)` — fabricar un guardado desde cero.
- [ ] **TOOL-07** `Catalog.ScanAsync` — reconstruir el índice leyendo **solo preámbulos**.
- [ ] **TOOL-08** `VerifyAsync` en lote sobre una carpeta.
- [ ] **TOOL-09** API de adopción: comparar dos archivos cualesquiera, decir si son compatibles y
      **por qué no** cuando no lo son, y adoptar bajo petición.
- [ ] **TOOL-10** Exportar e importar el contenedor como archivo portátil o cadena pegable.
      Lo que se exporta **es** un `.etm`, así que importar pasa por las mismas comprobaciones.
- [ ] **TOOL-11** **Generador de claves**: material de CSPRNG, toma el siguiente `keyId` libre y
      **se niega a sobrescribir uno existente**. Emite el conjunto completo —actual y retiradas,
      con su estado— como **archivo de código troceado**, para el repositorio privado del juego.
- [ ] **TOOL-12** Tamaño de clave: **suelo de 32 bytes impuesto por el paquete**, elegible por el
      proyecto hasta 64. Avisar si se pide más: HMAC reduce con hash cualquier clave mayor que el
      bloque de SHA-256, así que por encima de 64 bytes **no compra nada**.
- [ ] **TOOL-13** **Orden de los miembros en `EternalNode`.** Hoy usa un diccionario y no conserva
      el orden, así que reescribir un guardado desde una herramienta baraja los campos y deja un
      diff ilegible. No corrompe nada; hace inútil comparar dos guardados. Se decide **aquí** y no
      antes, cuando ya se sabe si las ventanas editan un árbol o un texto: arreglarlo antes de
      saberlo podía ser trabajo tirado. → cierra la deuda de la decisión **4**

> **Hecho cuando:** las pruebas de Tooling pasan sin referenciar Unity.

---

## Fase 7 — Ventanas de Unity · **Hito H3**

Clientes finos. **Regla no negociable: ninguna ventana usa acceso `internal`.** Si necesita algo,
se añade a la API pública de Tooling — es lo que permite que otro motor haga su propia capa.

- [ ] **EDIT-01** **Save Browser**: lista ámbitos, ranuras y registros con metadatos, **sin cargar
      cuerpos**.
- [ ] **EDIT-02** Browser: entradas dañadas en rojo, con opción de borrar o restaurar del `.bak`.
- [ ] **EDIT-03** **Save Inspector**: preámbulo, metadatos, veredicto de integridad y cuerpo
      editable campo a campo.
- [ ] **EDIT-04** Inspector: guardar reempaqueta y **refirma**.
- [ ] **EDIT-05** **Export / Import plano**: volcar a JSON legible y volver a empaquetar.
- [ ] **EDIT-06** **Integrity Report**: `VerifyAsync` sobre una carpeta, en tabla.
- [ ] **EDIT-07** **Save Forge**: crear un guardado desde cero o desde plantilla. Para QA: «justo
      antes del jefe final», «con todo desbloqueado».
- [ ] **EDIT-08** **Load into Play**: marcar un guardado como pendiente para que el juego lo cargue
      al entrar en play.
- [ ] **EDIT-09** **Migration Tool**: cargar dos guardados lado a lado, ver sus identidades,
      versiones de juego y esquemas, y **por qué** uno no es adoptable si no lo es.
- [ ] **EDIT-10** Migration Tool: forzar la adopción de una carpeta heredada elegida a mano, para
      los casos que el barrido automático no cubre.
- [ ] **EDIT-11** **Key Manager**: ver el conjunto de claves y su estado, **generar la siguiente**,
      y degradar una de `Active` → `ReadOnly` → `Warn` → `Rejected`. Con un aviso explícito en el
      paso a `Rejected` de que **deja fuera a quien no haya jugado desde la rotación**.

> **Hecho cuando:** QA puede fabricar una partida en un estado concreto y entrar en play con ella,
> y se puede recuperar a mano un guardado de una versión con otro nombre.

---

## Fase 8 — Ranuras y catálogo *(implementado, no usado en este juego)*

- [ ] **SLOT-01** Ranuras reales sobre `SlotId`, con `SlotId.Default` como caso degenerado.
- [ ] **SLOT-02** Catálogo `catalog.etm` como **caché, no fuente de verdad**.
- [ ] **SLOT-03** Orden de escritura: **cuerpo primero (atómico), catálogo después**.
- [ ] **SLOT-04** Reconstrucción del catálogo escaneando `slots/*/` — un huérfano se repara solo.
- [ ] **SLOT-05** Descriptor de ranura: tipo, política de rotación, si el jugador puede
      sobrescribir o borrar.
- [ ] **SLOT-06** Autoguardado como **anillo de N** que rotan.
- [ ] **SLOT-07** Guardado rápido (ranura reservada) y manual (nombrada por el jugador).
- [ ] **SLOT-08** Miniaturas: offset y longitud en los metadatos, para poder saltar a ellas.
- [ ] **SLOT-09** Prueba: borrar una ranura **no** toca el progreso de cuenta.

> **Hecho cuando:** un proyecto de prueba con seis ranuras las lista, crea, borra y reanuda, y
> este juego sigue sin enterarse de que existen.

---

## Fase 9 — Cifrado *(implementado, no usado en este juego)*

- [ ] **CRY-01** Transformación `AES-256-CBC` (`0x02`) en `Eternal.Crypto`.
- [ ] **CRY-02** Orden **cifrar primero, firmar después**, sobre el texto cifrado.
- [ ] **CRY-03** Los metadatos **no** se cifran, pero **sí** quedan cubiertos por la firma.
- [ ] **CRY-04** Derivación e IV por archivo, nunca reutilizados.
- [ ] **CRY-05** Prueba de compatibilidad hacia adelante: un archivo escrito **sin** cifrado se
      lee correctamente en un build **con** el módulo activado.
- [ ] **CRY-06** Repetir `TST-09` (degradación) con el cifrado activo.

> **Hecho cuando:** activar el módulo no rompe ningún archivo anterior.

---

## Fase 10 — CLI

- [ ] **CLI-01** Ejecutable `dotnet` sobre `Eternal.Tooling`.
- [ ] **CLI-02** Comandos `verify`, `dump`, `repack`.
- [ ] **CLI-03** Trabajo de CI que **compila el CLI**. Si deja de compilar, alguien metió una
      dependencia de Unity donde no debía: es un test de arquitectura disfrazado de herramienta.
- [ ] **CLI-04** Documentar su uso para soporte: revisar el guardado que mande un jugador sin
      abrir el editor.
- [ ] **CLI-05** Comandos `key new` y `key list`, para poder rotar desde CI o desde una máquina sin
      Unity el día que haga falta.

---

## Fase 11 — Steam

Se implementan **las dos vías** y el proyecto que consuma el paquete elige la suya al arrancar, en
una línea de composición. No hay respuesta universal: Auto-Cloud mantiene las builds idénticas, la
API es inmune a los renombrados.

- [ ] **STEAM-01** Vía A — **Auto-Cloud**, por defecto. Cero código de Steam: las dos builds
      escriben en `persistentDataPath` y Steam sincroniza esa carpeta por configuración del panel.
- [ ] **STEAM-02** Congelar `Company Name` y `Product Name` **para la configuración de la nube**.
      Ojo: la adopción de `D-02` resuelve el disco local, pero **no** la nube — si cambia la ruta,
      Steam deja de sincronizar la vieja. Si hay que renombrar después de publicar, hay que
      actualizar también las entradas de Auto-Cloud.
- [ ] **STEAM-03** Entrada de Auto-Cloud para **Windows**: root `WinAppDataLocalLow`, subdirectorio
      `<Company>/<Product>`, patrón `*.etm`.
- [ ] **STEAM-04** Entrada para **macOS**: root `MacAppSupport`.
- [ ] **STEAM-05** Entrada para **Linux**: root `LinuxHome` — **ojo, la ruta tiene otra forma**
      (`.config/unity3d/`), así que el subdirectorio **no** es el mismo que en Windows y macOS. Es
      el fallo clásico que hace que solo Linux no sincronice.
- [ ] **STEAM-06** Verificar que `machine/` **no** tiene entrada y que `.part` **no** encaja en el
      patrón.
- [ ] **STEAM-07** Prueba real entre dos máquinas: guardar en una, abrir en la otra.
- [ ] **STEAM-08** Vía A — **supervivencia al renombrado**: mantener la entrada de ruta **vieja** y
      añadir la nueva. Steam restaura la vieja al arrancar, la adopción local la copia a la nueva,
      y Steam sube la nueva al salir. Retirar la entrada vieja tras unas versiones.
- [ ] **STEAM-09** Vía B — `SteamRemoteStorageStore` sobre `ISteamRemoteStorage` en el ensamblado
      `Eternal.Steam`. Identifica por **nombre plano, por AppID y usuario**, independiente del
      disco: **inmune a renombrar el juego**. Capacidades: sin `AtomicReplace` ni `RandomAccess`.
- [ ] **STEAM-10** Envoltorio libre para la vía B: **Steamworks.NET** (MIT, orientado a Unity) o
      **Facepunch.Steamworks** (MIT). Detrás de version define, para que no compile si no está.
- [ ] **STEAM-11** Selector de almacén en el arranque, y documentar la tabla de compromiso para que
      el desarrollador elija con criterio. Probar que cambiar de vía **no cambia el formato**: un
      `.etm` escrito por una lo lee la otra.

---

## Fase 12 — Binario *(solo cuando un juego lo pida)*

- [ ] **BIN-01** Adaptador MemoryPack con `SerializerId 0x02`.
- [ ] **BIN-02** `GenerateType.VersionTolerant` **obligatorio**, con `[MemoryPackOrder]` explícito
      en cada miembro.
- [ ] **BIN-03** Regla de equipo escrita: **nunca reutilizar un número de orden**. Un reordenado
      descuidado en un PR corrompe partidas de jugadores.
- [ ] **BIN-04** Registro manual de formateadores para tipos union — `ModuleInitializer` no
      funciona en Unity.
- [ ] **BIN-05** Prueba de convivencia: un build con binario **sigue leyendo** los `.etm` en JSON.
- [ ] **BIN-06** Migración perezosa: leer JSON, escribir binario. Sin conversión masiva ni día D.

---

## Fase 13 — Cierre y publicación · **Hito H4**

- [ ] **FIN-01** Revisar que nada de `Eternal.Core` ni `Eternal.Tooling` referencia `UnityEngine`.
- [ ] **FIN-02** Revisar el repositorio del paquete: etiquetas de versión, que la git URL apunte a
      una etiqueta y no a `main`, y que un proyecto limpio pueda instalarlo.
- [ ] **FIN-03** Versionado semántico y changelog al día.
- [ ] **FIN-04** Documentar el formato `.etm` como **especificación**, no solo como código.
- [ ] **FIN-05** **Prueba de reutilización**: adoptar el paquete en un proyecto vacío escribiendo
      *solo* un modelo de datos y su captura/restauración. Si obliga a tocar el paquete, la costura
      estaba mal puesta.
- [ ] **FIN-06** Retirar el aviso de API inestable **solo** cuando exista el juego 2.
- [ ] **FIN-07** Escribir el **procedimiento del día malo** como runbook: qué se hace paso a paso
      si una clave se filtra, y el compromiso del paso 4 —rechazar pronto corta la filtración pero
      deja fuera a los rezagados; rechazar tarde no deja a nadie fuera pero mantiene la puerta
      abierta— para que esa decisión se tome informada y no con prisa.

---

## Extensiones diseñadas y **no** implementadas

Están pensadas y documentadas para que añadirlas sea mecánico, pero **ninguna se construye ahora**.
Se listan aquí para que no se redescubran desde cero dentro de dos años, y porque saber que caben
es lo que permite no construirlas hoy con la conciencia tranquila.

| extensión | ¿toca el formato? | cuándo tendría sentido |
|---|---|---|
| **Suelo de clave por ranura** | **no** — el `keyId` ya está en el preámbulo; solo añade un registro y una comprobación | un juego con progresión competitiva, donde cerrar la ventana entre rotar y retirar compense romper la restauración de copias viejas |
| **Serializador binario** (fase 12) | no — `serializerId` ya está reservado | repeticiones, snapshots de red, estado de mundo grande |
| **Almacenes de consola** | no — es otro `IStore` | el día que haya devkit |
| **Subclaves por tipo de registro** | no — HKDF las da con su parámetro `info` | si algún registro necesitase aislamiento criptográfico del resto |
| **Abstracción de nube propia** | no | cuando exista una **segunda** nube además de Steam |

Las cinco comparten una propiedad que no es casualidad: **ninguna obliga a cambiar el contenedor.**
Ese es el criterio que decide si algo puede esperar. Lo que sí habría que reservar hoy ya está
reservado — `keyId`, `serializerId`, los rangos de identificadores.

---

## Reglas que no se negocian

Están repartidas por el plan; se juntan aquí para que no se pierdan.

1. **Nunca se reutiliza un identificador** de transformación, serializador u orden de MemoryPack.
2. **Los archivos dorados viejos no se tocan.** Cada cambio de contenedor añade uno nuevo.
3. **El HMAC cubre el preámbulo**, no solo la carga.
4. **Las ventanas del editor no usan acceso privilegiado.** Si falta algo, se añade a la API pública.
5. **`OnApplicationQuit` no se usa** para guardar.
6. **Los ajustes de máquina no van a la nube.**
7. **Borrar una ranura no toca el progreso de cuenta.**
8. **El id de ranura no es su posición.**
9. **La API pública sigue inestable** hasta que exista un segundo consumidor real.
10. **El `productId` se genera una vez y no se toca nunca.** Es lo que permite renombrar el juego
    sin dejar tirada a la gente.
11. **El paquete no contiene ninguna clave.** El repositorio es público; la clave la pone el juego.
12. **Al adoptar se copia, nunca se mueve.** El original es la red de seguridad.
13. **Los cosméticos van en ámbito cuenta**, para que borrar una partida no borre las skins.
14. **El `keyId` va en el preámbulo desde el día uno**, exista una clave o veinte.
15. **Se escribe con la clave más nueva, se lee con la que declare el archivo.** Nunca al revés.
16. **Una clave retirada no se borra**, solo deja de firmar. Borrarla deja tirados los guardados
    que aún no se han refirmado.
17. **Cambiar de vía de Steam no cambia el formato.** Un `.etm` escrito con Auto-Cloud lo lee la
    API y al revés.
18. **El material de las claves no entra en el repositorio público** del paquete. Vive en el del
    juego, o en un secreto de CI. Lo que **sí** pone el paquete es el generador: el azar no se
    improvisa por proyecto.
19. **La derivación HKDF está congelada** igual que el formato: cambiarla invalidaría todas las
    claves existentes.
20. **Rechazar una clave es irreversible para quien no haya migrado.** Se decide con datos, no con
    prisa.
