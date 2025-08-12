# Theme Structure Guide for FFXIManager

## Resource Hierarchy

### 1. Theme Files (LightTheme.xaml, DarkTheme.xaml)
These should contain ONLY color/brush definitions that change between themes:
- Base colors (Background, Surface, Text, etc.)
- Button colors (Primary, Success, Warning, etc.)
- All brushes that need to change when switching themes

### 2. App.xaml
Should contain:
- Style definitions (using DynamicResource for theme-aware properties)
- Control templates
- Non-themed resources (constants, converters, etc.)
- NO color definitions (all colors come from themes)

## Best Practices

1. **Use DynamicResource** for all theme-dependent properties
2. **Use StaticResource** only for referencing styles or non-themed resources
3. **Keep color definitions in themes only** - no fallback colors in App.xaml
4. **Both themes must define ALL required resources** - no relying on defaults
5. **Remove unused/legacy resources** to keep files clean

## Resource Naming Convention

### Colors
- Use descriptive names: BackgroundColor, SurfaceColor, PrimaryTextColor
- Suffix with "Color" for Color resources

### Brushes
- Match color names but use "Brush" suffix: BackgroundBrush, SurfaceBrush
- Button brushes: PrimaryBrush, PrimaryHoverBrush, PrimaryPressedBrush

### Styles
- Use descriptive names: ModernButtonBase, PrimaryButton, IconButton
- Suffix with control type when not obvious

## Theme Switching
- Themes are switched by replacing the ResourceDictionary at index 0
- All DynamicResource bindings automatically update
- No need to refresh individual controls
