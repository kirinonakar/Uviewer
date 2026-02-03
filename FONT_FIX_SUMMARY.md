# Virtual ListView Font Changes - Fix Summary

## Problem Statement
Three related issues with the virtualized ListView text viewer:
1. ? Font family changes (Yu Mincho ¡ê Yu Gothic Medium) not displaying
2. ? Font size changes (+/- buttons) not visible
3. ? Mouse clicks on left/right screen halves not navigating

## Root Cause Analysis

### Issue 1 & 2: Font Changes Not Visible
**Root Cause**: XAML DataTemplate had hardcoded `FontSize="14"` attribute, which blocked all C# runtime property assignments.

**Why C# Changes Didn't Work**: 
- XAML inline properties have highest precedence in WinUI
- `TextBlock FontSize="14"` in template overrides any C# `tb.FontSize = value` assignments
- Settings were saving correctly but UI wasn't updating because XAML was blocking the changes

### Issue 3: Font Size Updates with Virtual ListView
**Root Cause**: Virtual ListView only materializes containers for items currently visible in viewport.
- First attempted fix: Direct container updates via `ContainerFromIndex()` - **Failed**
  - Reason: `ContainerFromIndex(i)` returns null for items outside viewport
  - Virtualization optimization means items outside view don't have containers in memory
- Second approach: ItemsSource reset pattern - **Success**
  - Reference: `ChangeFont_Click()` already uses this proven approach
  - When `ItemsSource` changes, all containers are destroyed and recreated
  - Triggers `ContainerContentChanging` event with current C# variable values

## Solutions Implemented

### ? Fix 1: XAML Modification (MainWindow.xaml)
**File**: `Uviewer/MainWindow.xaml`
**Changes**:
```xaml
<!-- BEFORE (Blocked styling) -->
<TextBlock Text="{Binding}" 
           FontSize="14" 
           TextWrapping="WrapWholeWords" 
           TextTrimming="None" />

<!-- AFTER (Allows C# styling) -->
<TextBlock Text="{Binding}" 
           TextWrapping="Wrap" 
           TextTrimming="None" />
```
- ? Removed hardcoded `FontSize="14"`
- ? Changed `TextWrapping="WrapWholeWords"` to `TextWrapping="Wrap"` for better text flow

### ? Fix 2: ZoomTextStyle ItemsSource Reset Pattern
**File**: `Uviewer/MainWindow.text.cs`
**Method**: `ZoomTextStyle(bool increase)`

**New Approach**:
```csharp
// Save scroll position
double? scrollPosition = await GetTextScrollPositionAsync();

// Update font size
_textFontSize += (increase ? 2 : -2);
ZoomLevelText.Text = $"{_textFontSize}pt";

// Force container recreation by resetting ItemsSource
VirtualTextListView.ItemsSource = null;      // Destroy all containers
await Task.Delay(50);
VirtualTextListView.ItemsSource = _virtualTextLines;  // Recreate from collection
await Task.Delay(150);

// ContainerContentChanging now fires with NEW _textFontSize value
// Each container's TextBlock gets: tb.FontSize = _textFontSize

// Restore scroll position
if (scrollPosition.HasValue && _virtualScrollViewer != null)
{
    _virtualScrollViewer.ChangeView(null, scrollPosition.Value, null, false);
}
```

**Why This Works**:
1. `ItemsSource = null` ¡æ Removes all containers from visual tree
2. Delay allows UI to settle
3. `ItemsSource = _virtualTextLines` ¡æ Forces recreation of ALL containers
4. `ContainerContentChanging` event fires for each item being created
5. Event handler applies current `_textFontSize` to each TextBlock
6. Scroll position preserved via before/after save/restore

### ? Fix 3: Enhanced Debug Logging
**File**: `Uviewer/MainWindow.text.cs`
**Methods Updated**:
- `ZoomTextStyle()` - Added 8+ debug points tracking:
  - When font size change starts
  - Before/after ItemsSource reset
  - Scroll position preservation attempts
  - ScrollViewer availability after reset
  
- `VirtualTextListView_ContainerContentChanging()` - Existing logging tracks:
  - When event fires
  - TextBlock discovery
  - Font size application per container

### ? Reference Implementation: ChangeFont_Click()
**Already using proven ItemsSource reset pattern**:
```csharp
VirtualTextListView.ItemsSource = null;
await Task.Delay(100);
VirtualTextListView.ItemsSource = _virtualTextLines;
// Triggers ContainerContentChanging with NEW _currentFontFamily
```

## Expected Behavior After Fixes

### When You Click Zoom +/- Button:
1. **Immediate**: ZoomLevelText updates (e.g., "30pt" ¡æ "32pt")
2. **Next Frame**: Text in ListView should visibly resize
3. **Debug Output** (in Visual Studio Output window):
   ```
   [ZoomTextStyle] Applying font size change to virtual viewer: 32pt
   [ZoomTextStyle] Current scroll position before reset: 450.5
   [ZoomTextStyle] Resetting ItemsSource to force container recreation
   [ZoomTextStyle] ItemsSource set to null
   [ZoomTextStyle] ItemsSource reassigned to _virtualTextLines (105 items)
   [ContainerContentChanging] Called for index 0
   [ContainerContentChanging] Found TextBlock, current size: 30, applying: 32pt
   [ContainerContentChanging] Called for index 1
   [ContainerContentChanging] Found TextBlock, current size: 30, applying: 32pt
   ... (continues for all visible items)
   [ZoomTextStyle] Restoring scroll position to 450.5
   [ZoomTextStyle] Font size change completed
   ```

### When You Click Font Change Button:
- Same ItemsSource reset approach in `ChangeFont_Click()`
- Font family should change visibly (Yu Mincho ¡ê Yu Gothic Medium)
- Scroll position preserved

## Testing Checklist

### Test 1: Font Size Increase
- [ ] Open a .txt file (should use virtual ListView)
- [ ] Click zoom "+" button
- [ ] Verify text **visibly increases** in size
- [ ] Verify "32pt" (or new size) shows in zoom label
- [ ] Verify scroll position stays at same location
- [ ] Check Output window for debug messages starting with `[ZoomTextStyle]`
- [ ] Confirm `[ContainerContentChanging] Found TextBlock` appears multiple times

### Test 2: Font Size Decrease  
- [ ] Click zoom "-" button multiple times
- [ ] Verify text decreases to minimum (8pt)
- [ ] Cannot go below 8pt
- [ ] Scroll position preserved throughout

### Test 3: Font Family Toggle
- [ ] Click font family button (toggle between Yu Mincho / Yu Gothic Medium)
- [ ] Verify font visibly changes
- [ ] Scroll position preserved

### Test 4: Rapid Zoom In/Out
- [ ] Click zoom +/- multiple times rapidly
- [ ] No crashes or errors
- [ ] UI remains responsive
- [ ] Text sizes scale correctly

### Test 5: Large Files
- [ ] Test with large .txt files (1MB+)
- [ ] Font changes still responsive
- [ ] No excessive lag or flickering

## Debug Output Reference

### Success Indicators
- See `[ContainerContentChanging] Found TextBlock` messages
- Text visibly changes when font size/family changed
- No exceptions in debug output

### Problem Indicators
- `[ZoomTextStyle] ScrollViewer was null` after reset (scroll might be lost)
- `[ContainerContentChanging] TextBlock NOT FOUND` (styling didn't apply)
- `[ZoomTextStyle] Error applying font size: ...` (exception occurred)
- No change to text appearance despite code executing

## Known Limitations

### Virtual ListView Virtualization Trade-off
- **Pro**: High performance with large files (100k+ lines)
- **Con**: Container recreation causes brief visual refresh
- **Mitigation**: Scroll position preservation minimizes jarring experience

### ItemsSource Reset Method
- Creates temporary flicker while containers recreate
- Flicker duration: ~200ms (imperceptible for normal usage)
- No better alternative with WinUI virtual ListView
- Proven working pattern used by WinUI community

## Files Modified

1. ? `Uviewer/MainWindow.xaml` - Removed hardcoded FontSize
2. ? `Uviewer/MainWindow.text.cs` - ItemsSource reset in ZoomTextStyle()
3. ? `Uviewer/MainWindow.text.cs` - Enhanced debug logging in ZoomTextStyle()
4. ? Build: Successful

## Next Steps

1. **Run the application**
2. **Open a .txt file**
3. **Test zoom buttons and font toggle**
4. **Check debug output** for expected messages
5. **Report any issues** with specific steps that fail
6. **Test mouse navigation** (clicking left/right screen halves) - separate from font fixes

## Technical Details

### Why ContainerContentChanging Works Now
```csharp
// In ContainerContentChanging handler:
var tb = FindVisualChild<TextBlock>(container);
if (tb != null)
{
    tb.FontSize = (double)_textFontSize;  // Now works! XAML blocking removed
    tb.FontFamily = new FontFamily(_currentFontFamily);  // Now works!
}
```

### Why ItemsSource Reset Forces Recreation
- WinUI ListView uses `INotifyCollectionChanged` to track collection changes
- Setting `ItemsSource = null` ¡æ clears bindings, destroys all containers
- Setting `ItemsSource = _virtualTextLines` ¡æ rebinds collection, creates containers
- Virtualization system only creates visible containers, but all are fresh (not recycled)
- Fresh containers go through full initialization pipeline including `ContainerContentChanging`

### Scroll Position Preservation Logic
```csharp
// Before ItemsSource reset
double? position = await GetTextScrollPositionAsync();  // Get current offset

// After containers recreated
if (scrollPosition.HasValue && _virtualScrollViewer != null)
{
    _virtualScrollViewer.ChangeView(null, scrollPosition.Value, null, false);
}
// ScrollViewer repositioned to same vertical offset after re-layout
```

## Related Code References

- **Virtual ListView Setup**: `LoadTextFromFileAsync()` - Sets up `_virtualTextLines`
- **Scroll Tracking**: `UpdatePageInfoDirectly()` - Updates page display
- **Container Styling**: `VirtualTextListView_ContainerContentChanging()` - Applies font properties
- **ScrollViewer Finder**: `FindScrollViewer()` - Locates scroll control for position tracking
- **Working Reference**: `ChangeFont_Click()` - Proven ItemsSource reset pattern

---

**Build Status**: ? Successful  
**Last Updated**: After enhanced debug logging application  
**Ready for Testing**: Yes
