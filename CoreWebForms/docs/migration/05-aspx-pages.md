# Phase 5: ASPX Page Migration

Fix ASPX markup for CoreWebForms SDK runtime compilation compatibility.

**Reference commit**: `f95e95e`

## Steps

### 1. Replace Bind() with Eval()

The CoreWebForms SDK runtime ASPX compiler does not support `Bind()`. Replace all two-way binding expressions with one-way `Eval()`.

**Before** (`Products.aspx`):
```aspx
<asp:TextBox ID="txtEditName" runat="server" Text='<%# Bind("Name") %>' />
<asp:DropDownList ID="ddlEditCategory" runat="server" SelectedValue='<%# Bind("Category") %>' />
<asp:TextBox ID="txtEditPrice" runat="server" Text='<%# Bind("Price") %>' />
<asp:TextBox ID="txtEditStock" runat="server" Text='<%# Bind("Stock") %>' />
<asp:CheckBox ID="chkEditActive" runat="server" Checked='<%# Bind("IsActive") %>' />
```

**After** (`Products.aspx`):
```aspx
<asp:TextBox ID="txtEditName" runat="server" Text='<%# Eval("Name") %>' />
<asp:DropDownList ID="ddlEditCategory" runat="server" SelectedValue='<%# Eval("Category") %>' />
<asp:TextBox ID="txtEditPrice" runat="server" Text='<%# Eval("Price") %>' />
<asp:TextBox ID="txtEditStock" runat="server" Text='<%# Eval("Stock") %>' />
<asp:CheckBox ID="chkEditActive" runat="server" Checked='<%# (bool)Eval("IsActive") %>' />
```

**Important**: Since `Bind()` is no longer used, values must be extracted manually from controls in the code-behind `RowUpdating` handler (which the existing code already does via `FindControl()`):

```csharp
protected void gvProducts_RowUpdating(object sender, GridViewUpdateEventArgs e)
{
    var row = gvProducts.Rows[e.RowIndex];
    var name = ((TextBox)row.FindControl("txtEditName")).Text;
    var category = ((DropDownList)row.FindControl("ddlEditCategory")).SelectedValue;
    // ... extract other values from controls
}
```

**Find all Bind() usage**:
```bash
grep -rn 'Bind(' Pages/ --include='*.aspx' --include='*.ascx'
```

### 2. Remove asp:UpdatePanel and AsyncPostBackTrigger

The CoreWebForms SDK does not support partial-page postbacks. Remove `UpdatePanel` wrappers and `AsyncPostBackTrigger` elements.

**Before** (`OrderWizard.ascx`):
```aspx
<asp:UpdatePanel ID="upCalendar" runat="server">
    <ContentTemplate>
        <asp:Calendar ID="calDelivery" runat="server" ... />
    </ContentTemplate>
    <Triggers>
        <asp:AsyncPostBackTrigger ControlID="ddlCustomer" EventName="SelectedIndexChanged" />
    </Triggers>
</asp:UpdatePanel>
```

**After**:
```aspx
<asp:Calendar ID="calDelivery" runat="server" ... />
```

The control falls back to full postback behavior. Preserve any JavaScript guards for graceful degradation:

```javascript
if (typeof Sys !== 'undefined' && typeof Sys.WebForms !== 'undefined') {
    Sys.WebForms.PageRequestManager.getInstance().add_endRequest(function() { ... });
}
```

**Find all UpdatePanel usage**:
```bash
grep -rn 'UpdatePanel\|AsyncPostBackTrigger' Pages/ --include='*.aspx' --include='*.ascx'
```

### 3. Remove asp:CustomValidator

The CoreWebForms SDK does not support the `OnServerValidate` event wiring on `CustomValidator`. Replace with manual validation.

**Before** (`OrderWizard.ascx`):
```aspx
<asp:CustomValidator ID="cvDate" runat="server" ControlToValidate="calDelivery"
    OnServerValidate="cvDate_ServerValidate" Display="Dynamic"
    ErrorMessage="Please select a future delivery date." />
```

**After**:
```aspx
<asp:Label ID="lblDateError" runat="server" ForeColor="Red" Visible="false"
    Text="Please select a future delivery date." />
```

Add manual validation logic in the code-behind button handler:

```csharp
protected void btnNext_Click(object sender, EventArgs e)
{
    lblDateError.Visible = false;
    if (calDelivery.SelectedDate < DateTime.Today)
    {
        lblDateError.Visible = true;
        return;
    }
    // ... continue processing
}
```

**Find all CustomValidator usage**:
```bash
grep -rn 'CustomValidator' Pages/ --include='*.aspx' --include='*.ascx'
```

### 4. Simplify web.config

Remove legacy IIS-specific configuration. Keep only CoreWebForms-required settings.

**Before** (~130 lines with binding redirects, targetFramework attributes, etc.)

**After** (24 lines):
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <add key="ValidationSettings:UnobtrusiveValidationMode" value="None" />
  </appSettings>
  <system.web>
    <hostingEnvironment shadowCopyBinAssemblies="false" />
    <authentication mode="None" />
    <authorization>
      <allow users="*" />
    </authorization>
    <compilation debug="true" />
    <httpRuntime />
    <httpCookies httpOnlyCookies="true" />
    <globalization responseEncoding="utf-8" fileEncoding="utf-8" requestEncoding="utf-8" />
    <customErrors mode="RemoteOnly" defaultRedirect="~/Pages/Errors/ErrorPage.aspx">
      <error statusCode="404" redirect="~/Pages/Errors/ErrorPage.aspx" />
    </customErrors>
  </system.web>
  <system.webServer>
    <modules runAllManagedModulesForAllRequests="true" />
    <directoryBrowse enabled="false" />
  </system.webServer>
</configuration>
```

**Removed**:
- All `targetFramework` attributes
- `<runtime>` assembly binding redirects
- `DbPath` app setting (moved to Program.cs)
- Default document config (handled by routing)
- `packages.config` references

**Changed**:
- `runAllManagedModulesForAllRequests`: `false` → `true` (required by CoreWebForms SDK)

## Unsupported Features Summary

| Feature | Status | Replacement |
|---|---|---|
| `Bind()` | Not supported | `Eval()` + manual `FindControl()` in code-behind |
| `UpdatePanel` | Not supported | Remove — full postback |
| `AsyncPostBackTrigger` | Not supported | Remove |
| `CustomValidator` (server-side) | Not supported | Label + manual validation |
| `ScriptManager` | Supported | Use `AddScriptManager()` + `MapScriptManager()` |

## Verification

```bash
dotnet build
dotnet run --project CoreWebForms.csproj
```

- Browse to each page and verify rendering
- Test edit/save operations (verify `Eval()` + `FindControl()` works)
- Test postback-heavy pages (Calendar, Wizard)
```
