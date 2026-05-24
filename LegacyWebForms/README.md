# LegacyWebForms

A complete ASP.NET WebForms 4.8 inventory management demo covering every major WebForms control and pattern. Built as Phase 1 of a CoreWebForms migration study — the goal is to have a realistic legacy app to migrate to .NET 9 via [CoreWebForms](https://github.com/dotnet/systemweb-adapters).

## Purpose

- Showcase all WebForms controls that CoreWebForms can support
- Provide a real, data-driven app (not a hello-world) to migrate in Phase 2
- Run locally via IIS Express without Visual Studio

## Pages

| Page | Route | Description |
|------|-------|-------------|
| Dashboard | `/` | Stats, recent orders, expandable category browser |
| Products | `/Products.aspx` | Browse by category + full CRUD grid |
| Orders | `/Orders.aspx` | Multi-step order wizard + order management |

## WebForms Controls Used

| Control | Where |
|---------|-------|
| `Literal` | Dashboard stat cards |
| `Repeater` + `AlternatingItemTemplate` | Dashboard recent orders, Orders history |
| `Repeater` (nested) + `DataList` | Products — active products by category |
| `Repeater` + `ItemCommand` | Dashboard — expandable category list |
| `BulletedList` | Dashboard out-of-stock, Orders confirmation summary |
| `DataList` + `AlternatingItemTemplate` | Products — products per category card grid |
| `GridView` — sort, page, select, inline edit, delete | Products management, Orders management |
| `DetailsView` | Products — row detail panel |
| `Panel` (toggle visibility) | Products add-product form |
| `MultiView` / `View` | Orders — 3-step wizard |
| `Calendar` | Orders — delivery date picker |
| `UpdatePanel` + `AsyncPostBackTrigger` | Orders — live estimated total |
| `RadioButtonList` | Orders — priority selection |
| `CheckBoxList` | Orders — add-ons selection |
| `DropDownList` (AutoPostBack) | Products filter, Orders product picker |
| `ValidationSummary` | Products add form, Orders step 1 |
| `RequiredFieldValidator` | Multiple fields across Products and Orders |
| `RangeValidator` | Price, stock, quantity fields |
| `RegularExpressionValidator` | Email field in Orders |
| `CustomValidator` | Orders — delivery date must be future |
| `LinkButton` | Products — toggle add form |

## Tech Stack

- **Framework**: ASP.NET WebForms on .NET 4.8
- **Database**: SQLite via `System.Data.SQLite.Core` 1.0.118.0
- **Native interop**: `Stub.System.Data.SQLite.Core.NetFramework` 1.0.118.0 (provides `SQLite.Interop.dll`)
- **Server**: IIS Express (x86) on port 8080
- **Build**: MSBuild 17 (VS 2022)
- **Package restore**: nuget.exe 7.6.x (`.nuget\nuget.exe`)
- **C# version**: `LangVersion: latest` (enables C# 8 switch expressions in .NET 4.8)

## Database

SQLite file created at `App_Data\inventory.db` on first run. Schema:

```
Products (Id, Name, Category, Price, Stock, IsActive, AddedDate)
Orders   (Id, CustomerName, CustomerEmail, ProductId, ProductName,
          Quantity, UnitPrice, OrderDate, DeliveryDate, Status, Priority, Extras)
```

Seeded with 12 products across 5 categories and 6 sample orders. `Extras` stored as comma-separated string.

## Project Structure

```
LegacyWebForms/
├── App_Data/               # SQLite DB created here at runtime
├── Properties/
│   └── AssemblyInfo.cs
├── .vscode/
│   └── tasks.json          # restore → build → run tasks
├── AppData.cs              # SQLite data access layer (models + CRUD)
├── Global.asax / .cs       # DB initialization on Application_Start
├── Site.Master / .cs       # Master page: nav, ScriptManager, CSS
├── Default.aspx / .cs      # Dashboard
├── Products.aspx / .cs     # Products browse + management
├── Orders.aspx / .cs       # Order wizard + order management
├── web.config              # UnobtrusiveValidationMode=None
├── packages.config
└── LegacyWebForms.csproj
```

## Running Locally

### Prerequisites

- Windows
- Visual Studio 2022 (for MSBuild + IIS Express)
- IIS Express installed (comes with VS 2022)

### First run

Restore NuGet packages and build:

```
# From repo root (D:\Programming\.Net\CoreWebForms)
.nuget\nuget.exe restore LegacyWebForms.sln
```

Or use the VS Code tasks (Ctrl+Shift+P → **Tasks: Run Task**):

| Task | Action |
|------|--------|
| `restore` | nuget.exe restore packages |
| `build` | MSBuild compile |
| `run` | build + launch IIS Express + open browser |

App runs at **http://localhost:8080**.

### NuGet packages location

Packages restore to `D:\Programming\.Net\CoreWebForms\packages\`. The SQLite native DLL (`SQLite.Interop.dll`) is copied to `bin\x86\` and `bin\x64\` automatically via the MSBuild targets file imported from the Stub package.

## Key Implementation Notes

- **`EmptyDataTemplate` not supported on `DataList`** — only `GridView` and `DetailsView` support it. DataList inside Repeaters omit the empty template.
- **`UpdatePanel` namespace**: `System.Web.UI.UpdatePanel`, not `System.Web.UI.WebControls.UpdatePanel`.
- **Validator jQuery requirement disabled**: `web.config` sets `ValidationSettings:UnobtrusiveValidationMode=None` so validators work without jQuery.
- **IIS Express runs x86**: loads `bin\x86\SQLite.Interop.dll`. The MSBuild targets import handles copying both x86 and x64 binaries.
- **C# Dev Kit warning**: VS Code's C# Dev Kit does not support traditional .NET Framework `.csproj` format. Intellisense is limited, but build and run via tasks.json work fine.

## Implementation Details

### Data Layer — `AppData.cs`

Single static class. No ORM. Raw ADO.NET via `System.Data.SQLite`.

- `Initialize(appDataPath)` — called from `Global.asax Application_Start`. Creates `App_Data/` if missing, builds connection string, runs `CREATE TABLE IF NOT EXISTS`, seeds once via `HasData()` count check.
- Every method opens a fresh `SQLiteConnection` and disposes it via `using`. No connection pooling or shared state beyond the connection string `_cs`.
- `decimal` ↔ SQLite `REAL`: stored as `(double)price`, read back as `(decimal)reader.GetDouble(...)`.
- `bool IsActive` ↔ SQLite `INTEGER`: stored as `1`/`0`, read as `GetInt32(...) == 1`.
- `DateTime` ↔ SQLite `TEXT`: stored as `"yyyy-MM-dd"` or `"yyyy-MM-dd HH:mm:ss"`, read via `DateTime.Parse(reader.GetString(...))`.
- `List<string> Extras` ↔ SQLite `TEXT`: stored as comma-joined string, split on read. Empty string → empty list.
- `last_insert_rowid()` appended to `INSERT` in same command text; `ExecuteScalar()` returns `long`, cast to `int`.

**Models:**

```csharp
class Product { int Id; string Name, Category; decimal Price; int Stock; bool IsActive; DateTime AddedDate; }
class Order   { int Id; string CustomerName, CustomerEmail, ProductName, Status, Priority;
                int ProductId, Quantity; decimal UnitPrice; DateTime OrderDate, DeliveryDate;
                List<string> Extras; decimal Total => Quantity * UnitPrice; }
```

**CRUD surface:**

| Method | SQL |
|--------|-----|
| `GetProducts()` | `SELECT * FROM Products ORDER BY Id` |
| `GetProduct(id)` | `SELECT * FROM Products WHERE Id=@id` |
| `AddProduct(p)` | `INSERT … SELECT last_insert_rowid()` |
| `UpdateProduct(p)` | `UPDATE Products SET … WHERE Id=@id` |
| `DeleteProduct(id)` | `DELETE FROM Products WHERE Id=@id` |
| `GetOrders()` | `SELECT * FROM Orders ORDER BY OrderDate DESC` |
| `GetOrder(id)` | `SELECT * FROM Orders WHERE Id=@id` |
| `AddOrder(o)` | `INSERT … SELECT last_insert_rowid()` |
| `UpdateOrder(o)` | `UPDATE Orders SET Status, Priority WHERE Id=@id` |
| `DeleteOrder(id)` | `DELETE FROM Orders WHERE Id=@id` |

---

### Dashboard — `Default.aspx`

**Stat cards** — four `<asp:Literal>` controls set in code-behind with counts from in-memory LINQ over `AppData.GetProducts()` / `AppData.GetOrders()`.

**Recent Orders Repeater** — `rptOrders` bound to `orders.Take(6)`. Uses `AlternatingItemTemplate` (alternating row background via inline style) to demonstrate both templates. Status displayed as plain text (no badges — keeps Dashboard read-only).

**Expandable Categories** — `rptCatExpand` (Repeater) bound to `AppData.Categories` (a `List<string>`). Each item renders:
- A `LinkButton` with `CommandName="toggle"` and `CommandArgument=category`
- An `<asp:Panel>` whose `Visible` is set via `<%# (bool)Eval("Expanded") %>`
- An `<asp:Literal>` emitting an HTML `<ul>` of product names via `GetProductList(Eval("ProductNames"))`

Anonymous type used as data source: `new { Category, Count, Expanded, ProductNames }`. `ViewState["ExpandedCat"]` stores the currently expanded category; clicking the same category again collapses it (toggle). `ItemCommand` handler updates ViewState and rebinds without full `BindDashboard()`.

**Out of Stock** — `blOutOfStock` (`BulletedList`, `BulletStyle="Disc"`). Shows product names with `Stock == 0`, or a single "All products in stock" item.

---

### Products — `Products.aspx`

**Browse by Category section** — outer `rptCategories` (Repeater) iterates `AppData.Categories`. `OnItemDataBound` handler finds the inner `dlCatProducts` (DataList) via `FindControl`, filters `AppData.GetProducts()` to active products in that category, and binds. DataList uses `RepeatColumns="4"` horizontal layout with `AlternatingItemTemplate` for alternating card background. `EmptyDataTemplate` is NOT used (DataList does not support it).

**Add Product Panel** — `pnlAdd` hidden by default. `lnkAddProduct` (LinkButton) toggles `pnlAdd.Visible`. Panel contains `ValidationSummary` + `RequiredFieldValidator` / `RangeValidator` on all fields, scoped to `ValidationGroup="AddProduct"`. On save: parses price/stock, inserts via `AppData.AddProduct`, clears fields, shows `lblAddResult` success message, rebinds grid.

**Filter row** — `ddlFilter` (category DropDownList, `AutoPostBack=true`) + `chkActiveOnly` (CheckBox, `AutoPostBack=true`, default checked). Both fire `ddlFilter_Changed` → resets `PageIndex` to 0, rebinds grid.

**GridView (`gvProducts`)** — `DataKeyNames="Id"`. Sort state in `ViewState["SortField"]` / `ViewState["SortDir"]`. Sort uses C# 8 switch expression over field name. Paging: `PageSize=5`. Columns:
- `BoundField` for Id (ReadOnly)
- `TemplateField` for Name (TextBox in edit), Category (DropDownList in edit with `SelectedValue='<%# Bind("Category") %>'`), Price (TextBox), Stock (TextBox), Status (badge span / CheckBox)
- `CommandField` with Select + Edit + Delete buttons

`RowUpdating`: reads edit controls via `FindControl`, loads product from DB, updates fields, calls `AppData.UpdateProduct`. `RowDeleting`: deletes from DB, resets to page 0.

**DetailsView (`dvProduct`)** — shown inside `pnlDetail` when a GridView row is selected. Bound to a single-element anonymous-type array with formatted strings (e.g. `Price.ToString("C")`). `AutoGenerateRows=true`. Hidden on filter change, delete, or edit entry.

---

### Orders — `Orders.aspx`

**Layout** — two-column top section (wizard left, history right) plus full-width Manage Orders grid below.

**Order Wizard — `MultiView` / `View`**

Three `<asp:View>` inside `mvOrder`. Active view controlled by `SetStep(int step)` which sets `mvOrder.ActiveViewIndex` and toggles CSS classes on `pnlStep1/2/3` (step indicator Panels).

- **Step 1 (Order Info)**:
  - `ddlProduct` (DropDownList, `AutoPostBack=true`) → `Product_Changed` → `RefreshTotal()`
  - `txtQty` (TextBox, `AutoPostBack=true`) → `Product_Changed` → `RefreshTotal()`
  - `upTotal` (UpdatePanel, `UpdateMode=Conditional`) wraps `litTotal`. Triggers on both controls → partial-page refresh showing live estimated total without full postback.
  - `txtCustomerName`, `txtEmail` with `RequiredFieldValidator` and `RegularExpressionValidator` (`\S+@\S+\.\S+`)
  - `rblPriority` (RadioButtonList, horizontal flow): Low / Normal (default) / High
  - `cblExtras` (CheckBoxList, horizontal): Gift wrap / Express delivery / Insurance
  - `calDelivery` (Calendar, day selection mode) + `cvDate` (CustomValidator): `cvDate_ServerValidate` checks `calDelivery.SelectedDate > DateTime.Today`
  - All fields in `ValidationGroup="Step1"`. `btnNext` triggers server validation before advancing.

- **Step 2 (Review)**: Read-only table of `Literal` controls (`litRevProduct`, `litRevQty`, etc.) populated in `btnNext_Click`. Back button returns to step 0. Place Order button (no validation) calls `btnConfirm_Click`.

- **Step 3 (Confirmed)**: Shows order ID in `litOrderId`. `blSummary` (BulletedList) lists order details. "Place Another Order" button resets all fields and returns to step 0.

**`btnConfirm_Click`**: constructs `Order` object, calls `AppData.AddOrder`, populates step 3 controls, rebinds history Repeater, calls `SetStep(2)`.

**Order History Repeater (`rptHistory`)** — right-side panel, read-only. Bound to `AppData.GetOrders()`. `GetStatusBadge(string status)` (C# 8 switch expression on the page class) returns colored `<span class="badge ...">` HTML used in `<%# GetStatusBadge(Eval("Status").ToString()) %>`.

**Manage Orders GridView (`gvOrders`)** — full-width below. Sort via `ViewState["OrderSortField"]`/`"OrderSortDir"`. Edit mode exposes `ddlEditStatus` (Pending/Processing/Shipped/Delivered) and `ddlEditPriority` (Low/Normal/High). `RowDataBound` handler pre-selects current values by calling `Items.FindByValue(...).Selected = true` for both dropdowns. `RowUpdating` loads order from DB via `AppData.GetOrder(id)`, updates Status + Priority, saves. `RowDeleting` also rebinds the history Repeater so both views stay in sync.

---

### Master Page — `Site.Master`

Contains `<asp:ScriptManager ID="ScriptManager1" runat="server" />` — required globally for `UpdatePanel` and client-side validators. Two `ContentPlaceHolder`s: `HeadContent` (for page-specific head tags) and `MainContent` (page body).

Nav links use `runat="server"` on `<a>` tags so ASP.NET resolves `~/` relative to app root. CSS is inline in the master page: dark blue `#1e3a5f` header, white card system, badge classes (`.badge-green/blue/yellow/gray`), grid table styles, wizard step indicators, and product card layout.

---

## Phase 2

Migrate this project to .NET 9 using CoreWebForms (`Microsoft.AspNetCore.SystemWebAdapters`), which wraps the existing `.aspx` / `.aspx.cs` files to run on ASP.NET Core.
