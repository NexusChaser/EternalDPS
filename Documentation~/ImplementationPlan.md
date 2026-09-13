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
| 1 | Núcleo: contenedor y tubería | 25 | ⬜ |
| 2 | Serialización y almacén de archivo | 14 | ⬜ |
| 3 | **Integración mínima en este juego** | 11 | ⬜ |
| 4 | Pruebas | 17 | ⬜ |
| 5 | WebGL | 9 | ⬜ |
| 6 | Tooling sin motor | 12 | ⬜ |
| 7 | Ventanas de Unity | 11 | ⬜ |
| 8 | Ranuras y catálogo | 9 | ⬜ |
| 9 | Cifrado | 6 | ⬜ |
| 10 | CLI | 5 | ⬜ |
| 11 | Steam (**las dos vías**) | 11 | ⬜ |
| 12 | Binario | 6 | ⬜ |
| 13 | Cierre y publicación | 7 | ⬜ |

**152 tareas. Las 5 decisiones, cerradas.**

**Hitos:**
- **H1 — este juego ya persiste** → al terminar la fase 3. Es el primer punto con valor real.
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

- [ ] **CORE-01** `Scope` (Machine · Account · Slot), `RecordKind` (Settings · Progress · Session).
- [ ] **CORE-02** `SlotId` — identificador **opaco y estable** (GUID), con `SlotId.Default`.
      Nombre y orden de visualización son datos aparte, nunca el id.
- [ ] **CORE-03** `EternalKey = (Scope, RecordKind, RecordId, SlotId)` + composición de ruta con
      colapso de segmentos ausentes.
- [ ] **CORE-04** `SaveProfile`: `Lifetime`, `Cloud`, `OnMigrationFailure`, `OnIntegrityFailure`,
      `Conflict`, `Backups`.
- [ ] **CORE-05** `LoadResult` como resultado cerrado con los nueve veredictos de la §12 de la
      arquitectura. **No excepciones** para casos esperados.
- [ ] **CORE-06** `IntegrityReport` (veredicto + qué región falló + tamaños leídos).

### Interfaces

- [ ] **CORE-07** `ISerializer` (con `FormatId`), `IDocumentSerializer` (opcional, árbol editable),
      `IByteTransform` (con `TransformId` e inversa), `IClock`, `IEternalLog`.
- [ ] **CORE-08** `IStore` **asíncrono** + `StoreCapabilities`
      (`AtomicReplace`, `List`, `Delete`, `RandomAccess`).

### Contenedor `.etm`

- [ ] **CORE-09** `IKeyProvider`: el **juego** aporta las claves, el paquete **no trae ninguna**
      (repo público). El núcleo falla ruidosamente si se le pide firmar sin clave. Nunca una
      cadena literal — aparece haciendo `strings` al binario. → ver `CORE-21..25`
- [ ] **CORE-10** Escritor del preámbulo: magia `ETM1`, `containerVersion`, flags, longitudes,
      `serializerId`, lista ordenada de `transformIds`.
- [ ] **CORE-11** Bloque de metadatos: **`productId`**, `schemaVersion`, `savedAtUtc`,
      `appVersion`, `slotName`, `playtime`, offsets de miniatura, campos libres del juego.
- [ ] **CORE-12** Escritura del cuerpo aplicando la cadena de transformaciones **en orden**.
- [ ] **CORE-13** Firma `HMAC-SHA256` sobre **todo lo anterior, preámbulo incluido** (cierra la
      degradación descrita en §5).
- [ ] **CORE-14** Lector: parsear preámbulo **sin aplicar ninguna transformación**, y leer
      metadatos sin tocar el cuerpo.
- [ ] **CORE-15** Verificación **en flujo**, sin deserializar el cuerpo (alimenta `VerifyAsync`).
- [ ] **CORE-16** Registros de `TransformId` y `SerializerId`, con los rangos reservados y la regla
      de **no reutilizar nunca un número**.
- [ ] **CORE-17** Transformación `Deflate` (`0x01`).
- [ ] **CORE-18** Fachada `EternalDataDriver`: `SaveAsync`, `LoadAsync`, `DeleteAsync`,
      `ExistsAsync`, `VerifyAsync`. Orquesta serializador + transformaciones + almacén.
- [ ] **CORE-19** `ProductIdentity`: el `productId` actual (GUID **congelado de por vida**) más la
      lista de **identidades heredadas** aceptadas. → sale de `D-02`
- [ ] **CORE-20** `CanAdopt(bytes)`: comprueba en orden magia → `containerVersion` → `productId` →
      camino de migración, **sin deserializar el cuerpo**, y devuelve el motivo cuando dice que no.
- [ ] **CORE-21** Byte **`keyId`** en el preámbulo, en offset 13. Se reserva **aunque solo exista
      una clave**: añadirlo después sería subir la versión del contenedor. → sale de `D-03`
- [ ] **CORE-22** Conjunto de claves: **escribir siempre con la más nueva, leer con la que declare
      el archivo**. El `IKeyProvider` entrega la actual más las retiradas.
- [ ] **CORE-23** **Refirmado automático**: un archivo cargado con clave retirada se reescribe con
      la actual en el siguiente guardado. Silencioso, el jugador no hace nada.
- [ ] **CORE-24** Política de retirada por clave: `Active` → `ReadOnly` → `Warn` → `Rejected`.
- [ ] **CORE-25** Derivación con **HKDF** en el paquete; el material de entrada lo pone el juego.

> **Hecho cuando:** se puede escribir y volver a leer un `byte[]` con un `ISerializer` y un
> `IStore` falsos en memoria, el HMAC detecta un bit cambiado, y `CanAdopt` reconoce un archivo
> escrito bajo otro nombre de producto.

---

## Fase 2 — Serialización y almacén de archivo

- [ ] **SER-01** Adaptador Newtonsoft, `FormatId 0x01`, implementando también `IDocumentSerializer`.
- [ ] **SER-02** Ajustes de Json.NET: cultura invariante, **`TypeNameHandling` desactivado**
      (agujero de deserialización conocido), fechas en UTC ISO-8601.
- [ ] **SER-03** Patrón de polimorfismo con `JsonConverter` propio y **campo discriminador
      explícito**, como alternativa segura a `TypeNameHandling`.
- [ ] **STO-01** `FileStore` asíncrono con todas las capacidades.
- [ ] **STO-02** Escritura atómica: `.part` → reemplazo. La extensión `.part` es deliberada para que
      el patrón `*.etm` de Steam **no** la recoja.
- [ ] **STO-03** Rotación de copias `.bak` según `SaveProfile.Backups`.
- [ ] **STO-04** Listado y borrado, con claves inexistentes tratadas como caso normal.
- [ ] **STO-05** Barrido de **carpetas hermanas** bajo la raíz de datos, buscando `.etm` adoptables
      con `CORE-20`. Acotado a hermanas: nunca se recorre el disco entero.
- [ ] **STO-06** Adopción automática al primer arranque si la carpeta actual está vacía:
      **copiar, nunca mover**, escribir marca para no repetirlo, quedarse con la candidata de
      `savedAtUtc` más reciente si hay varias, y dejar constancia en el log.
- [ ] **UNI-01** `Eternal.Unity`: resolución de rutas sobre `Application.persistentDataPath` con la
      disposición `machine/` · `account/` · `slots/`.
- [ ] **UNI-02** Arranque del sistema y registro de serializadores y transformaciones disponibles.
- [ ] **UNI-03** Disparadores de guardado: `OnApplicationPause(true)` y `OnApplicationFocus(false)`.
      **Nunca `OnApplicationQuit`** — en Android y WebGL no se llama de forma fiable.
- [ ] **UNI-04** Antirrebote, para no escribir en cada movimiento de un slider.
- [ ] **UNI-05** `link.xml` en el paquete preservando `JsonConvert` y `DefaultContractResolver`.

> **Hecho cuando:** un modelo de prueba se guarda en disco desde el editor, se relee tras reiniciar
> Unity, y el archivo es binario ilegible en un editor de texto.

---

## Fase 3 — Integración mínima en este juego · **Hito H1**

Esta fase es la que convierte el trabajo en valor. **Recordatorio: hoy el juego no persiste nada
en build** — ni el volumen ni el idioma. Se comprobó: cero usos de `PlayerPrefs`,
`persistentDataPath` o serialización en el código de juego, y los cambios de `CycloneMemory`
solo sobreviven en el editor porque Unity serializa el asset.

- [ ] **GAME-01** Definir los registros de este juego según la matriz: `machine/display`,
      `account/prefs`. Progreso, de momento, no se crea; el cargador trata «no existe» como
      «progreso nuevo». Los cosméticos futuros irán en **ámbito cuenta**, nunca en ranura — o
      borrar una partida borraría las skins. → cierra `D-05`
- [ ] **GAME-02** Modelo de estado de partida: turno, posiciones, puntuaciones, inventarios,
      **minijuego en curso** y semilla del RNG. **No se serializa nada interno de los
      minijuegos**: al reanudar se vuelve a ese minijuego y se reinicia desde cero.
      Con `schemaVersion` desde el primer commit. → cierra `D-04`
- [ ] **GAME-03** Asmdef propio para los modelos de datos, para preservarlo entero en `link.xml`
      en vez de ir tipo por tipo.
- [ ] **GAME-04** Persistir los **cuatro volúmenes** y enlazarlos con `CycloneMemory` al arrancar.
- [ ] **GAME-05** Persistir el **idioma** y aplicarlo antes de que se resuelva la primera cadena
      localizada.
- [ ] **GAME-06** Persistir **calidad gráfica y resolución** en `machine/` — ámbito de máquina,
      nunca de nube.
- [ ] **GAME-07** Captura y restauración de la partida. El punto de guardado es **al entrar al
      minijuego, con el tablero tal como estaba antes de sus recompensas** — así reiniciar el
      minijuego no puede repartir premios dos veces.
- [ ] **GAME-08** Verificar en una **build real** (no solo en el editor) que volumen e idioma
      sobreviven al cierre.
- [ ] **GAME-09** Generar el `productId` de este juego, congelarlo, y enganchar la adopción
      automática de `STO-06`.
- [ ] **GAME-10** Generar el material de clave `keyId 1`, implementar el `IKeyProvider` del juego
      en su propio ensamblado, y **decidir quién lo custodia** y dónde (no en el repo público del
      paquete). → cierra la parte pendiente de `D-03`
- [ ] **GAME-11** Exportar e importar guardado desde el juego: a archivo y a cadena pegable.
      Es la única vía entre orígenes distintos y entre plataformas distintas.

> **Hecho cuando:** cierras el juego compilado, lo vuelves a abrir y el volumen, el idioma y la
> calidad gráfica siguen donde los dejaste.

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
