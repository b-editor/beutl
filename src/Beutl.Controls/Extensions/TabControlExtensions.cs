using System.Collections;
using Avalonia.Controls;

namespace Beutl.Controls.Extensions;

public static class TabControlExtensions
{
    public static void CloseTab(this TabControl tabControl, TabItem tabItem)
    {
        try
        {
            if (tabItem == null)
            {
            }
            else
            {
                //var n_index = NewIndex(tabControl, tabItem);
                ((IList)tabControl.ItemsSource).Remove(tabItem); //removes the tabitem itself
                                                           //tabControl.SelectedIndex = n_index;
            }
        }
        catch (Exception e)
        {
            throw new Exception("The TabItem does not exist", e);
        }
        finally
        {
        }
    }

    public static void CloseTab(this TabControl tabControl, int index)
    {
        index--;
        try
        {
            if (index < 0)
            {
            }
            else
            {
                //var item = (tabControl.Items as List<TabItem>).Select(x => x.IsSelected == true);
                //tabControl.SelectedIndex = NewIndex(tabControl, index);
                ((IList)tabControl.ItemsSource).RemoveAt(index);
            }
        }
        catch (Exception e)
        {
            throw new Exception("the index must be greater than 0", e);
        }
    }

    public static bool AddTab(this TabControl tabControl, TabItem TabItemToAdd, bool Focus = true)
    {
        try
        {
            //Thanks to Grokys this is possible
            ((IList)tabControl.ItemsSource).Add(TabItemToAdd);
            switch (Focus)
            {
                case true:
                    TabItemToAdd.IsSelected = true;
                    break;
            }

            return true;
        }
        catch (SystemException e)
        {
            throw new SystemException("The Item to add is null", e);
        }
    }
}
