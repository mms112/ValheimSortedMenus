using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using static InventoryGui;

namespace SortedMenus
{
    [HarmonyPatch]
    internal class InventoryGuiPatch
    {
        internal static Dictionary<string, string> craftingStationSortingOverwrites = new Dictionary<string, string>();

        // higher than normal due to AAA crafting paginator
        [HarmonyPriority(Priority.HigherThanNormal)]
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList)), HarmonyPrefix]
        private static void UpdateRecipeList(InventoryGui __instance, ref List<Recipe> recipes)
        {
            if (!Player.m_localPlayer)
            {
                return;
            }

            CraftingStation currentCraftingStation = Player.m_localPlayer.GetCurrentCraftingStation();

            string stationName = currentCraftingStation ? Utils.GetPrefabName(currentCraftingStation.gameObject) : null;

            if (stationName != null && craftingStationSortingOverwrites.TryGetValue(stationName, out string sortingType))
            {
                switch (sortingType.ToLower())
                {
                    case "ignored":
                        return;

                    case "cooking":
                        CookingMenuSorting.UpdateRecipeList(ref recipes, stationName, __instance.InCraftTab());
                        return;

                    default:
                        break;
                }
            }

            if (stationName == "piece_cauldron")
            {
                CookingMenuSorting.UpdateRecipeList(ref recipes, stationName, __instance.InCraftTab());
            }
            else
            {
                CraftingMenuSorting.UpdateRecipeList(ref recipes, stationName, !currentCraftingStation);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList)), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> DisableVanillaSort(IEnumerable<CodeInstruction> instructions)
        {
            //Workaround for a bug in the harmony transpiler, which will always leave a try/catch block to the first instruction after the block (labels are ignored)
            //Insert a jump instruction to fix this
            var codes = new List<CodeInstruction>(instructions);
            bool foundInCraftTab = false;
            Label? leaveLabel = null;

            for (int i = 0; i < codes.Count; i++)
            {
                if (!foundInCraftTab)
                {
                    if (codes[i].Calls(typeof(InventoryGui).GetMethod(nameof(InventoryGui.InCraftTab))))
                    {
                        foundInCraftTab = true;
                    }
                }
                else
                {
                    if (leaveLabel == null && codes[i].opcode == OpCodes.Leave && codes[i].operand is Label)
                    {
                        leaveLabel = (Label)(codes[i].operand);
                    }

                    if (leaveLabel!= null && codes[i].opcode == OpCodes.Endfinally)
                    {
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Br, leaveLabel));
                        break;
                    }
                }
            }

            bool found_sortcraft = false;

            for (int i = 0; i < codes.Count; i++)
            {
                if (found_sortcraft)
                {
                    if (codes[i].opcode == OpCodes.Switch)
                    {
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                        codes.Insert(i + 2, new CodeInstruction(OpCodes.Ldflda, AccessTools.Field(typeof(InventoryGui), nameof(InventoryGui.m_availableRecipes))));
                        codes.Insert(i + 3, new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo((List<RecipeDataPair> data) => SortOverwrite(ref data))));
                        break;
                    }
                }
                else
                {
                    if (codes[i].opcode == OpCodes.Ldstr && (codes[i].operand as string) == "sortcraft")
                    {
                        found_sortcraft = true;
                        for (int j = i - 1; j >= 0; j--)
                        {
                            if (codes[j].opcode == OpCodes.Ldc_I4_0)
                            {
                                codes[j].opcode = OpCodes.Ldc_I4_M1;
                                break;
                            }
                        }
                    }
                }
            }

            return codes.AsEnumerable();
        }

        private static void SortOverwrite(ref List<RecipeDataPair> data)
        {
            data = data.OrderBy(item => !item.CanCraft).ThenBy(item => item.Recipe.m_listSortWeight).ToList();
        }
    }
}