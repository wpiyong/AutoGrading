   M S B A M L   `   `   `  ����F  BAutoGrading, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null)clr-namespace:AutoGradingAutoGrading  5      5clr-namespace:AutoGrading.ModelAutoGrading.Model  5          AutoGrading.App   5      R NWindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35W SPresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35D 9http://schemas.microsoft.com/winfx/2006/xaml/presentation    5      R NSystem.Xaml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5      +localclr-namespace:AutoGrading     5      /logclr-namespace:AutoGrading.Model     5        �� 	Resources  5         ToolbarButton �� 
TargetType �� Triggers P� Property P� Value �� Property �� Value  LogViewerStyle K� IsItemsHost �� DataType    AutoGrading.Model.LogEntry	 � IsSharedSizeScope
 � ColumnDefinitions z� SharedSizeGroup �� StringFormat �� 
IsReadOnly �� TextWrapping+   %AutoGrading.Model.CollapsibleLogEntry � RowDefinitions d�Name ��Name �� Triggers P� 
SourceName �� 
TargetName��%�  &	        &	 �     (�� |    5$      *, +)(�� �    53      *, +)7  �� 5	   
   " ��6&    5
      P� 5       � {� .  . �5      ��  �@J�Opacity$ .5Q�60   5      5      �� 5   
   " ��6   .�5      ��  � �� 5      �� 5      .R�5      �� ��. .��5      �� 5      5      5      �� 5       � �� 5      �� 5      .R�5      $�  . 61   5      5!      5"      �� 5$   
   "  6   .R�5%      � 	 . 
 5&      �� 5'      $
 Date��6'   $
��Auto �6>   �� 5(      .+�5+      �� $��0��6M   $
��Bold"�5,      $��5,0,5,0q�6*   ��5+      �� *
DateTime+$ HH:mm:ss��5,   *   �� 5.      $��1��63    . $��0q�6U   $
 Wrap=�5/      ��5.      �� *	Message+5/      50      51      �� 53   
   "  6   .R�54      � 	 . 
 55      �� 56      $
 Date��6'   $
��Auto �6>   �� 57       5:      �� 5;      $
��Auto �6$   �� 5<      .+�5?      �� $��0��66   $
��Bold"�5@      $��5,0,5,0q�6*   ��5?      �� *
DateTime+5@   *   �� 5B      $��1��63    . $��0q�6U   $
 Wrap=�5C      ��5B      �� *	Message+5C      d� 5E      $ Expander��6   $��1��61   $��0��6>   $	��Top=�5F      $��+��63   $��Right=�6?   �� 5H      $ Contents��5J      8��  $��1��5I      $��1��6(   $��2��68   $y�	Collapsed=�5J   -   ��5H      �� *
Contents+5J   -   5L       5M      P� 5N      $ Expander��6    �@d�	IsChecked$
 TrueY�6E   . �5O      �� $ Contents��6    � y�$ Visible=�6I   �� 5P      $ Expander��6    � ��$ -��6F   5Q      5S      5T      5U      